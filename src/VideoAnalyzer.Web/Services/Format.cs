using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Web.Services;

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    public static string LocalTime(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Position in the video, e.g. 1:05 or 1:02:05.</summary>
    public static string VideoTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    public static string Duration(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min"
            : t.TotalMinutes >= 1 ? $"{t.Minutes} min {t.Seconds} s"
            : $"{t.TotalSeconds:0.#} s";
    }

    /// <summary>"ALL_CAPS" Rekognition values to "All caps".</summary>
    public static string Words(string? value) =>
        string.IsNullOrEmpty(value) ? "–" : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant().Replace('_', ' ');

    public static string StatusCss(JobStatus status) => status switch
    {
        JobStatus.Uploading => "text-bg-secondary",
        JobStatus.Uploaded => "text-bg-info",
        JobStatus.Processing => "text-bg-primary",
        JobStatus.Completed => "text-bg-success",
        JobStatus.Failed => "text-bg-danger",
        _ => "text-bg-light",
    };

    public static bool IsFinal(JobStatus status) => status is JobStatus.Completed or JobStatus.Failed;
}
