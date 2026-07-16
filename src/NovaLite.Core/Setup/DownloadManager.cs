using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace NovaLite.Setup;

public class DownloadManager
{
    private readonly HttpClient _http;

    public DownloadManager()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLite/1.0 (Windows; User)");
    }

    public async Task DownloadModelAsync(IReadOnlyList<string> urls, string expectedSha256, string destinationPath, Action<double> progressCallback, CancellationToken ct = default)
    {
        Exception? lastException = null;

        foreach (var url in urls)
        {
            try
            {
                await TryDownloadWithResumeAsync(url, destinationPath, progressCallback, ct);
                
                progressCallback(100);
                
                // Verify SHA256
                if (!string.IsNullOrEmpty(expectedSha256))
                {
                    progressCallback(101); // Magic number for "Verifying"
                    bool valid = await VerifySha256Async(destinationPath, expectedSha256, ct);
                    if (!valid)
                    {
                        File.Delete(destinationPath);
                        throw new Exception("SHA-256 hash mismatch. File corrupted.");
                    }
                }
                
                return; // Success
            }
            catch (Exception ex)
            {
                lastException = ex;
                // Try next mirror
            }
        }

        throw new Exception($"All mirrors failed. Last error: {lastException?.Message}");
    }

    private async Task TryDownloadWithResumeAsync(string url, string destinationPath, Action<double> progressCallback, CancellationToken ct)
    {
        var fileInfo = new FileInfo(destinationPath);
        long existingLength = fileInfo.Exists ? fileInfo.Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Already fully downloaded
            return;
        }
        
        response.EnsureSuccessStatusCode();

        long totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingLength;
        if (totalBytes == existingLength) return;

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationPath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[81920];
        long totalRead = existingLength;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            totalRead += bytesRead;
            progressCallback((double)totalRead / totalBytes * 100);
        }
    }

    private async Task<bool> VerifySha256Async(string filePath, string expectedHash, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return actualHash == expectedHash.ToLowerInvariant();
    }
}
