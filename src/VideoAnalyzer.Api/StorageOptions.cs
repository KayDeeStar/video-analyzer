using System.ComponentModel.DataAnnotations;

namespace VideoAnalyzer.Api;

/// <summary>Bound from the "Storage" config section (env vars Storage__MediaBucket, ...).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string MediaBucket { get; set; } = "";

    [Range(1, 10_240)]
    public int MaxUploadSizeMb { get; set; } = 2048;

    /// <summary>Multipart part size. S3 requires at least 5 MB for every part but the last.</summary>
    [Range(5, 512)]
    public int PartSizeMb { get; set; } = 16;

    [Range(5, 720)]
    public int UploadUrlExpiryMinutes { get; set; } = 60;

    [Range(1, 720)]
    public int DownloadUrlExpiryMinutes { get; set; } = 15;

    public long MaxUploadBytes => MaxUploadSizeMb * 1024L * 1024L;
}
