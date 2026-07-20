using NovaLite.Core.Settings;
using Xunit;

namespace NovaLite.Tests;

public class AppSettingsTests
{
    [Fact]
    public void GetAutoLoadModelPath_PrefersLastModelPath_WhenItExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var modelPath = Path.Combine(tempDir, "preferred.gguf");
            File.WriteAllText(modelPath, "fake");

            var settings = new AppSettings
            {
                LastModelPath = modelPath,
                ModelDirectory = tempDir
            };

            var result = settings.GetAutoLoadModelPath();

            Assert.Equal(modelPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetAutoLoadModelPath_FallsBackToModelDirectory_WhenLastPathMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var firstModel = Path.Combine(tempDir, "model.gguf");
            File.WriteAllText(firstModel, "fake");

            var settings = new AppSettings
            {
                ModelDirectory = tempDir,
                LastModelPath = string.Empty
            };

            var result = settings.GetAutoLoadModelPath();

            Assert.Equal(firstModel, result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
