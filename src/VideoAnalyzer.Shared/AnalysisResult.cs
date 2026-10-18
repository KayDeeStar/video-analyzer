namespace VideoAnalyzer.Shared;

// Analysis results stored as results/{jobId}/result.json and returned by GET /api/jobs/{jobId}/results.
// Detection times are offsets into the video in milliseconds. All attributes are machine estimates.

/// <param name="Report">Description written by the vision model; null if it could not produce one.</param>
/// <param name="ReportModel">Bedrock model that wrote the report.</param>
/// <param name="People">When people are visible, from Amazon Rekognition person detection.</param>
/// <param name="Labels">Objects, scenes and activities detected by Amazon Rekognition (low-confidence noise removed).</param>
public sealed record AnalysisResult(
    string JobId,
    DateTimeOffset AnalyzedAt,
    VideoInfo Video,
    AiReport? Report,
    string? ReportModel,
    PeopleSummary People,
    IReadOnlyList<LabelSummary> Labels);

public sealed record VideoInfo(long DurationMs, double FrameRate, int Width, int Height, string? Codec, string? Format);

/// <param name="MaxVisibleAtOnce">Most people detected in a single moment.</param>
/// <param name="Appearances">Periods in which at least one person is visible.</param>
public sealed record PeopleSummary(int MaxVisibleAtOnce, IReadOnlyList<TimeRange> Appearances);

public sealed record TimeRange(long StartMs, long EndMs);

/// <summary>Position in the frame as fractions (0..1) of the frame width/height.</summary>
public sealed record BoundingBox(double Left, double Top, double Width, double Height);

public sealed record LabelSummary(
    string Name,
    IReadOnlyList<string> Categories,
    double MaxConfidence,
    int Detections,
    int MaxInstances,
    long FirstSeenMs,
    long LastSeenMs);
