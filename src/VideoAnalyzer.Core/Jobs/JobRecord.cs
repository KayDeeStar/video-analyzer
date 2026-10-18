using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Core.Jobs;

/// <summary>Stored as jobs/{jobId}/job.json in the media bucket.</summary>
public sealed record JobRecord
{
    public required string JobId { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public required string ContentType { get; init; }

    /// <summary>S3 key of the uploaded video, e.g. uploads/{jobId}/original.mp4.</summary>
    public required string VideoKey { get; init; }

    /// <summary>S3 multipart upload id; only meaningful while Uploading.</summary>
    public string? UploadId { get; init; }

    public required JobStatus Status { get; init; }
    public string? Error { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    public JobSummary ToSummary() => new(JobId, FileName, FileSize, Status, CreatedAt, UpdatedAt);

    public JobDetails ToDetails(string? videoUrl) =>
        new(JobId, FileName, FileSize, Status, CreatedAt, UpdatedAt, Error, videoUrl);
}
