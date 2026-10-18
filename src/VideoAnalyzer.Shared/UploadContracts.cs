namespace VideoAnalyzer.Shared;

/// <summary>Body of POST /api/uploads.</summary>
public sealed record CreateUploadRequest(string FileName, long FileSize);

/// <summary>
/// Returned by POST /api/uploads. The client PUTs byte range
/// [(PartNumber - 1) * PartSize, PartNumber * PartSize) of the file to each part URL,
/// keeps the ETag response header of each PUT, then calls the complete endpoint.
/// </summary>
public sealed record CreateUploadResponse(
    string JobId,
    long PartSize,
    IReadOnlyList<UploadPartUrl> Parts,
    DateTimeOffset ExpiresAt);

public sealed record UploadPartUrl(int PartNumber, string Url);

/// <summary>Body of POST /api/uploads/{jobId}/complete.</summary>
public sealed record CompleteUploadRequest(IReadOnlyList<CompletedPart> Parts);

public sealed record CompletedPart(int PartNumber, string ETag);
