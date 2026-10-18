namespace VideoAnalyzer.Shared;

/// <summary>One row in GET /api/jobs.</summary>
public sealed record JobSummary(
    string JobId,
    string FileName,
    long FileSize,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>GET /api/jobs/{jobId}.</summary>
/// <param name="VideoUrl">Short-lived pre-signed URL to stream the uploaded video, once uploaded.</param>
public sealed record JobDetails(
    string JobId,
    string FileName,
    long FileSize,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error,
    string? VideoUrl);
