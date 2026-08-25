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

    public async Task<long> DownloadModelAsync(IReadOnlyList<string> urls, string expectedSha256, string destinationPath, Action<double> progressCallback, CancellationToken ct = default)
    {
        Exception? lastException = null;

        foreach (var url in urls)
        {
            try
            {
                await TryDownloadWithResumeAsync(url, destinationPath, progressCallback, ct);

                progressCallback(100);

                // Verify SHA256 against the final destination file
                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    progressCallback(101); // Magic number for "Verifying"
                    bool valid = await VerifySha256Async(destinationPath, expectedSha256, ct);
                    if (!valid)
                    {
                        if (File.Exists(destinationPath))
                            File.Delete(destinationPath);
                        throw new Exception("SHA-256 hash mismatch. File corrupted.");
                    }
                }

                return new FileInfo(destinationPath).Length; // Success
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
        // Use a temporary "partial" file while downloading so incomplete files are easy to detect
        var tempPath = destinationPath + ".partial";
        var finalInfo = new FileInfo(destinationPath);
        long existingLength = 0;
        if (File.Exists(tempPath)) existingLength = new FileInfo(tempPath).Length;
        else if (finalInfo.Exists) existingLength = finalInfo.Length; // treat already-complete final file as existing

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Already fully downloaded
            if (File.Exists(destinationPath))
                return;

            if (File.Exists(tempPath))
            {
                File.Move(tempPath, destinationPath, true);
                return;
            }

            throw new Exception("Download already marked complete but destination file is missing.");
        }
        
        response.EnsureSuccessStatusCode();

        bool shouldAppend = existingLength > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (existingLength > 0 && response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            // Server ignored the requested range. Restart the file from scratch.
            shouldAppend = false;
            existingLength = 0;
        }

        long contentLength = response.Content.Headers.ContentLength ?? -1;
        long totalBytes = contentLength > 0 ? contentLength + existingLength : -1;
        if (totalBytes == existingLength && existingLength > 0)
        {
            // Already fully downloaded — ensure final file exists
            if (File.Exists(destinationPath)) return;
            if (File.Exists(tempPath))
            {
                File.Move(tempPath, destinationPath, true);
                return;
            }
        }

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempPath, shouldAppend ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[81920];
        long totalRead = existingLength;
        int bytesRead;
        double lastReportedProgress = -1;
        DateTimeOffset lastUpdateAt = DateTimeOffset.MinValue;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            totalRead += bytesRead;

            if (totalBytes > 0)
            {
                double progressPercent = (double)totalRead / totalBytes * 100;
                var now = DateTimeOffset.UtcNow;
                bool shouldReport = progressPercent - lastReportedProgress >= 0.5 ||
                                    (now - lastUpdateAt).TotalMilliseconds >= 250;

                if (shouldReport)
                {
                    lastReportedProgress = progressPercent;
                    lastUpdateAt = now;
                    progressCallback(progressPercent);
                }
            }
        }

        // Ensure the final file exists after a successful download.
        if (File.Exists(destinationPath))
            return;

        if (!File.Exists(tempPath))
            throw new Exception($"Download completed but temp file is missing: {tempPath}");

        try
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);

            File.Move(tempPath, destinationPath, true);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to finalize download file from '{tempPath}' to '{destinationPath}'", ex);
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
