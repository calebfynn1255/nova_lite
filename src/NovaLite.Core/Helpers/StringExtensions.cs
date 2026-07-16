namespace NovaLite.Core.Helpers;

public static class StringExtensions
{
    /// <summary>Truncates a string to <paramref name="maxLength"/> characters, appending an ellipsis if truncated.</summary>
    public static string Truncate(this string s, int maxLength, string suffix = "…") =>
        s.Length <= maxLength ? s : s[..maxLength] + suffix;

    /// <summary>Returns <c>null</c> if the string is null or whitespace, otherwise the original value.</summary>
    public static string? NullIfEmpty(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Converts a byte count to a human-readable string (KB / MB / GB).</summary>
    public static string ToFileSizeString(this long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
        _                => $"{bytes / 1024.0:F0} KB"
    };
}
