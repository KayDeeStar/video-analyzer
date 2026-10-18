namespace VideoAnalyzer.Shared;

// Analysis results stored as results/{jobId}/result.json and returned by GET /api/jobs/{jobId}/results.
// All times are offsets into the video in milliseconds. All attributes are machine estimates.

public sealed record AnalysisResult(
    string JobId,
    DateTimeOffset AnalyzedAt,
    VideoInfo Video,
    PeopleSummary People,
    IReadOnlyList<FaceObservation> Faces,
    int TotalFaceDetections,
    IReadOnlyList<LabelSummary> Labels,
    IReadOnlyList<TextSummary> Texts,
    OnScreenClock? OnScreenClock);

public sealed record VideoInfo(long DurationMs, double FrameRate, int Width, int Height, string? Codec, string? Format);

/// <param name="MaxVisibleAtOnce">Most people detected in a single moment.</param>
/// <param name="Appearances">Periods in which at least one person is visible.</param>
public sealed record PeopleSummary(int MaxVisibleAtOnce, IReadOnlyList<TimeRange> Appearances);

public sealed record TimeRange(long StartMs, long EndMs);

/// <summary>Position in the frame as fractions (0..1) of the frame width/height.</summary>
public sealed record BoundingBox(double Left, double Top, double Width, double Height);

/// <summary>One clear sighting of a face, with Rekognition's estimated attributes.</summary>
public sealed record FaceObservation(
    long TimestampMs,
    BoundingBox Box,
    double Confidence,
    int? AgeLow,
    int? AgeHigh,
    string? Gender,
    double? GenderConfidence,
    string? Emotion,
    bool? Eyeglasses,
    bool? Sunglasses,
    bool? Beard,
    bool? Mustache,
    bool? Smile,
    double? Sharpness,
    double? Yaw);

public sealed record LabelSummary(
    string Name,
    IReadOnlyList<string> Categories,
    double MaxConfidence,
    int Detections,
    int MaxInstances,
    long FirstSeenMs,
    long LastSeenMs);

public sealed record TextSummary(string Text, double Confidence, int Detections, long FirstSeenMs, long LastSeenMs);

/// <summary>Date/time overlay burned into the footage by the camera/DVR, if one was read.</summary>
public sealed record OnScreenClock(string FirstText, long FirstSeenMs, string LastText, long LastSeenMs);
