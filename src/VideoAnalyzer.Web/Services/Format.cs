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
