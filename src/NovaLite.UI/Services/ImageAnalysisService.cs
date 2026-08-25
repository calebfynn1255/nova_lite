using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace NovaLite.UI.Services;

/// <summary>
/// Provides rich image analysis for attached images, producing structured text
/// that text-only LLMs can reason about. Extracts metadata, dominant colors,
/// OCR text, EXIF data, and content type classification.
/// </summary>
public static class ImageAnalysisService
{
    /// <summary>
    /// Analyze an image file and return a structured text description
    /// suitable for injection into an LLM prompt.
    /// </summary>
    public static async Task<string> AnalyzeImageAsync(string filePath)
    {
        var sb = new StringBuilder();
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var fileInfo = new FileInfo(filePath);

        // Basic file info
        sb.AppendLine($"File: {Path.GetFileName(filePath)}");
        sb.AppendLine($"Size: {FormatFileSize(fileInfo.Length)}");

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var memStream = new MemoryStream();
            await fileStream.CopyToAsync(memStream);
            var bytes = memStream.ToArray();

            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);

            // Image dimensions and format
            int width = (int)decoder.PixelWidth;
            int height = (int)decoder.PixelHeight;
            string aspectRatio = GetAspectRatioLabel(width, height);
            string orientation = width > height ? "landscape" : (width < height ? "portrait" : "square");

            sb.AppendLine($"Dimensions: {width}x{height} ({aspectRatio} {orientation})");
            sb.AppendLine($"Format: {ext.TrimStart('.').ToUpperInvariant()}, {decoder.BitmapPixelFormat}");

            // Content type classification
            string contentType = ClassifyContentType(width, height, ext);
            sb.AppendLine($"Detected Content Type: {contentType}");

            // Color analysis via pixel sampling
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var colorInfo = AnalyzeColors(softwareBitmap);
            sb.AppendLine($"Brightness: {colorInfo.Brightness}");
            sb.AppendLine($"Dominant Colors: {string.Join(", ", colorInfo.DominantColors)}");

            if (!string.IsNullOrEmpty(colorInfo.BackgroundHint))
                sb.AppendLine($"Background: {colorInfo.BackgroundHint}");

            // EXIF metadata (if JPEG)
            if (ext == ".jpg" || ext == ".jpeg")
            {
                var exifInfo = await ExtractExifAsync(decoder);
                if (!string.IsNullOrEmpty(exifInfo))
                {
                    sb.AppendLine();
                    sb.AppendLine("EXIF Metadata:");
                    sb.AppendLine(exifInfo);
                }
            }

            // OCR text extraction
            var ocrText = await ExtractOcrTextAsync(softwareBitmap);
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                sb.AppendLine();
                sb.AppendLine("Text found in image (via OCR):");
                sb.AppendLine(ocrText.Trim());
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("No readable text detected in image.");
            }

            // Semantic vision captioning via ONNX (Phi-3-Vision)
            var caption = await OnnxVisionCaptioner.GenerateCaptionAsync(filePath);
            if (!string.IsNullOrWhiteSpace(caption))
            {
                sb.AppendLine();
                sb.AppendLine("Visual description:");
                sb.AppendLine(caption);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[Analysis error: {ex.Message}]");
        }

        return sb.ToString();
    }

    // ── Color Analysis ───────────────────────────────────────────────────────

    private record ColorAnalysisResult(
        string Brightness,
        List<string> DominantColors,
        string? BackgroundHint);

    private static ColorAnalysisResult AnalyzeColors(SoftwareBitmap bitmap)
    {
        try
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            // Read all pixels
            var buffer = new byte[width * height * 4]; // BGRA8
            bitmap.CopyToBuffer(buffer.AsBuffer());

            // Sample pixels in a grid pattern for efficiency
            int sampleStep = Math.Max(1, Math.Min(width, height) / 40);
            var sampledPixels = new List<(byte R, byte G, byte B)>();
            var cornerPixels = new List<(byte R, byte G, byte B)>();

            for (int y = 0; y < height; y += sampleStep)
            {
                for (int x = 0; x < width; x += sampleStep)
                {
                    int idx = (y * width + x) * 4;
                    if (idx + 2 < buffer.Length)
                    {
                        byte b = buffer[idx];
                        byte g = buffer[idx + 1];
                        byte r = buffer[idx + 2];
                        sampledPixels.Add((r, g, b));

                        // Collect corner pixels for background detection
                        bool isCorner = (x < width / 10 || x > width * 9 / 10) &&
                                        (y < height / 10 || y > height * 9 / 10);
                        if (isCorner)
                            cornerPixels.Add((r, g, b));
                    }
                }
            }

            if (sampledPixels.Count == 0)
                return new ColorAnalysisResult("Unknown", new List<string> { "Unknown" }, null);

            // Calculate average brightness
            double avgBrightness = sampledPixels.Average(p => (p.R * 0.299 + p.G * 0.587 + p.B * 0.114) / 255.0);
            string brightnessLabel = avgBrightness switch
            {
                < 0.2 => "Very dark",
                < 0.4 => "Dark",
                < 0.6 => "Medium",
                < 0.8 => "Bright",
                _ => "Very bright"
            };

            // Find dominant colors via simple color bucketing
            var dominantColors = GetDominantColors(sampledPixels, 5);

            // Background hint from corners
            string? bgHint = null;
            if (cornerPixels.Count > 10)
            {
                var avgCorner = (
                    R: (int)cornerPixels.Average(p => p.R),
                    G: (int)cornerPixels.Average(p => p.G),
                    B: (int)cornerPixels.Average(p => p.B)
                );
                var cornerBrightness = (avgCorner.R * 0.299 + avgCorner.G * 0.587 + avgCorner.B * 0.114) / 255.0;

                // Check if corners are relatively uniform (suggesting a solid background)
                double cornerVariance = cornerPixels.Average(p =>
                    Math.Pow(p.R - avgCorner.R, 2) + Math.Pow(p.G - avgCorner.G, 2) + Math.Pow(p.B - avgCorner.B, 2));

                if (cornerVariance < 2000) // Low variance = uniform background
                {
                    bgHint = cornerBrightness < 0.3
                        ? $"Dark/black background ({ColorToHex(avgCorner.R, avgCorner.G, avgCorner.B)})"
                        : cornerBrightness > 0.85
                            ? $"White/light background ({ColorToHex(avgCorner.R, avgCorner.G, avgCorner.B)})"
                            : $"Solid background ({ColorToHex(avgCorner.R, avgCorner.G, avgCorner.B)}, {DescribeColor(avgCorner.R, avgCorner.G, avgCorner.B)})";
                }
            }

            return new ColorAnalysisResult(brightnessLabel, dominantColors, bgHint);
        }
        catch
        {
            return new ColorAnalysisResult("Unknown", new List<string> { "Could not analyze" }, null);
        }
    }

    private static List<string> GetDominantColors(List<(byte R, byte G, byte B)> pixels, int maxColors)
    {
        // Simple color bucketing: quantize to 32-level per channel
        var buckets = new Dictionary<(int, int, int), int>();
        foreach (var p in pixels)
        {
            var key = (p.R / 32, p.G / 32, p.B / 32);
            buckets[key] = buckets.GetValueOrDefault(key, 0) + 1;
        }

        return buckets
            .OrderByDescending(kv => kv.Value)
            .Take(maxColors)
            .Select(kv =>
            {
                int r = kv.Key.Item1 * 32 + 16;
                int g = kv.Key.Item2 * 32 + 16;
                int b = kv.Key.Item3 * 32 + 16;
                string hex = ColorToHex(r, g, b);
                string name = DescribeColor(r, g, b);
                int pct = (int)(kv.Value * 100.0 / pixels.Count);
                return $"{name} ({hex}, {pct}%)";
            })
            .ToList();
    }

    private static string ColorToHex(int r, int g, int b) =>
        $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";

    private static string DescribeColor(int r, int g, int b)
    {
        double brightness = (r * 0.299 + g * 0.587 + b * 0.114) / 255.0;

        if (brightness < 0.1) return "Black";
        if (brightness > 0.9 && Math.Abs(r - g) < 30 && Math.Abs(g - b) < 30) return "White";
        if (Math.Abs(r - g) < 25 && Math.Abs(g - b) < 25) // Gray
            return brightness < 0.4 ? "Dark gray" : "Light gray";

        // Determine hue
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        if (max == min) return "Gray";

        double hue;
        if (max == r)
            hue = (double)(g - b) / (max - min);
        else if (max == g)
            hue = 2.0 + (double)(b - r) / (max - min);
        else
            hue = 4.0 + (double)(r - g) / (max - min);

        hue *= 60;
        if (hue < 0) hue += 360;

        string prefix = brightness < 0.35 ? "Dark " : (brightness > 0.75 ? "Light " : "");

        return hue switch
        {
            < 15 or >= 345 => prefix + "red",
            < 45 => prefix + "orange",
            < 75 => prefix + "yellow",
            < 150 => prefix + "green",
            < 195 => prefix + "cyan",
            < 260 => prefix + "blue",
            < 300 => prefix + "purple",
            _ => prefix + "pink"
        };
    }

    // ── Content Type Classification ──────────────────────────────────────────

    private static string ClassifyContentType(int width, int height, string ext)
    {
        double ratio = (double)width / height;

        // Common screenshot resolutions
        bool isScreenRes = (width == 1920 && height == 1080) ||
                           (width == 2560 && height == 1440) ||
                           (width == 3840 && height == 2160) ||
                           (width == 1366 && height == 768) ||
                           (width == 1280 && height == 720) ||
                           (width == 1024 && height == 768) ||
                           (width == 2560 && height == 1600) ||
                           (width == 1440 && height == 900);

        if (ext == ".png" && isScreenRes)
            return "Screenshot (matches common screen resolution)";

        if (ext == ".png" && ratio > 1.5 && ratio < 1.85)
            return "Likely screenshot or UI capture";

        if (ratio > 0.65 && ratio < 0.85)
            return "Likely document or portrait photo";

        if (width > 3000 && height > 2000)
            return "High-resolution photo";

        if (width < 500 && height < 500)
            return "Small image / icon / thumbnail";

        if (ext == ".jpg" || ext == ".jpeg")
            return "Photo (JPEG)";

        return "General image";
    }

    // ── EXIF Extraction ──────────────────────────────────────────────────────

    private static async Task<string> ExtractExifAsync(BitmapDecoder decoder)
    {
        try
        {
            var props = await decoder.BitmapProperties.GetPropertiesAsync(new[]
            {
                "System.Photo.CameraManufacturer",
                "System.Photo.CameraModel",
                "System.Photo.DateTaken",
                "System.Photo.FocalLength",
                "System.Photo.ISOSpeed",
                "System.Photo.ExposureTime",
                "System.Photo.FNumber",
                "System.GPS.Latitude",
                "System.GPS.Longitude"
            });

            var sb = new StringBuilder();
            void TryAdd(string key, string label)
            {
                if (props.ContainsKey(key) && props[key].Value != null)
                    sb.AppendLine($"  {label}: {props[key].Value}");
            }

            TryAdd("System.Photo.CameraManufacturer", "Camera Make");
            TryAdd("System.Photo.CameraModel", "Camera Model");
            TryAdd("System.Photo.DateTaken", "Date Taken");
            TryAdd("System.Photo.FocalLength", "Focal Length");
            TryAdd("System.Photo.ISOSpeed", "ISO");
            TryAdd("System.Photo.ExposureTime", "Exposure");
            TryAdd("System.Photo.FNumber", "F-Number");

            if (props.ContainsKey("System.GPS.Latitude") && props["System.GPS.Latitude"].Value != null &&
                props.ContainsKey("System.GPS.Longitude") && props["System.GPS.Longitude"].Value != null)
            {
                sb.AppendLine($"  GPS: {props["System.GPS.Latitude"].Value}, {props["System.GPS.Longitude"].Value}");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ── OCR ──────────────────────────────────────────────────────────────────

    private static async Task<string> ExtractOcrTextAsync(SoftwareBitmap bitmap)
    {
        try
        {
            var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocrEngine == null)
                return "[Windows OCR engine not available]";

            var result = await ocrEngine.RecognizeAsync(bitmap);
            return result.Text;
        }
        catch (Exception ex)
        {
            return $"[OCR error: {ex.Message}]";
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetAspectRatioLabel(int w, int h)
    {
        if (w == 0 || h == 0) return "unknown";

        double ratio = (double)w / h;

        return ratio switch
        {
            > 1.7 and < 1.85 => "16:9",
            > 1.55 and < 1.65 => "16:10",
            > 1.3 and < 1.4 => "4:3",
            > 0.95 and < 1.05 => "1:1",
            > 0.55 and < 0.65 => "9:16",
            > 0.7 and < 0.8 => "3:4",
            _ => $"{ratio:F2}:1"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
