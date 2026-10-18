namespace VideoAnalyzer.Shared;

/// <summary>Video file types accepted for upload (shared by the UI and the API).</summary>
public static class VideoFormats
{
    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".avi", ".mkv", ".wmv", ".mpg", ".mpeg", ".ts", ".mts", ".3gp", ".flv", ".webm",
    };

    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName));
}
