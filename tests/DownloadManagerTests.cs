using NovaLite.Setup;
using Xunit;

namespace NovaLite.Tests;

public class DownloadManagerTests
{
    [Fact]
    public async Task DownloadModelAsync_PropagatesCancellationWithoutMirrorFallback()
    {
        var manager = new DownloadManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"nova-lite-test-{Guid.NewGuid():N}.gguf");

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                manager.DownloadModelAsync(new[] { "https://example.invalid/model.gguf" }, string.Empty, tempFile, _ => { }, cts.Token));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
