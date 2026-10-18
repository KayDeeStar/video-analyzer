using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing;

// Compact, SDK-independent copies of Rekognition detections. ResultBuilder works on these
// (so it is unit-testable), and they are saved to work/{jobId}/detections.json for later steps.

/// <summary>One label (object/scene/activity) seen at one moment of the video.</summary>
public sealed record LabelSample(
    long TimestampMs,
    string Name,
    double Confidence,
    IReadOnlyList<string> Categories,
    IReadOnlyList<BoundingBox> Boxes);

/// <summary>One LINE of text read at one moment of the video.</summary>
public sealed record TextSample(long TimestampMs, string Text, double Confidence);

/// <summary>Raw detections kept for later pipeline steps (frame extraction, AI descriptions).</summary>
public sealed record Detections(
    IReadOnlyList<LabelSample> PersonFrames,
    IReadOnlyList<FaceObservation> Faces);
