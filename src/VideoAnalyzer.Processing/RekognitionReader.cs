using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using VideoAnalyzer.Shared;
using RekBoundingBox = Amazon.Rekognition.Model.BoundingBox;
using BoundingBox = VideoAnalyzer.Shared.BoundingBox;

namespace VideoAnalyzer.Processing;

/// <summary>Reads every page of finished Rekognition Video jobs and maps them to our sample types.</summary>
public sealed class RekognitionReader(IAmazonRekognition rekognition)
{
    private const int PageSize = 1000;

    public async Task<(VideoInfo Video, List<LabelSample> Labels)> ReadLabelsAsync(string jobId, CancellationToken ct)
    {
        var labels = new List<LabelSample>();
        VideoMetadata? metadata = null;
        string? next = null;
        do
        {
            var page = await rekognition.GetLabelDetectionAsync(new GetLabelDetectionRequest
            {
                JobId = jobId,
                MaxResults = PageSize,
                NextToken = next,
                SortBy = LabelDetectionSortBy.TIMESTAMP,
                AggregateBy = LabelDetectionAggregateBy.TIMESTAMPS,
            }, ct);
            metadata ??= page.VideoMetadata;
            labels.AddRange((page.Labels ?? [])
                .Where(d => d.Label is not null)
                .Select(d => new LabelSample(
                    d.Timestamp ?? 0,
                    d.Label.Name,
                    d.Label.Confidence ?? 0,
                    (d.Label.Categories ?? []).Select(c => c.Name).ToList(),
                    (d.Label.Instances ?? []).Where(i => i.BoundingBox is not null).Select(i => Map(i.BoundingBox)).ToList())));
            next = page.NextToken;
        } while (!string.IsNullOrEmpty(next));

        return (MapVideo(metadata), labels);
    }

    public async Task<List<FaceObservation>> ReadFacesAsync(string jobId, CancellationToken ct)
    {
        var faces = new List<FaceObservation>();
        string? next = null;
        do
        {
            var page = await rekognition.GetFaceDetectionAsync(new GetFaceDetectionRequest
            {
                JobId = jobId,
                MaxResults = PageSize,
                NextToken = next,
            }, ct);
            faces.AddRange((page.Faces ?? [])
                .Where(f => f.Face?.BoundingBox is not null)
                .Select(f => MapFace(f.Timestamp ?? 0, f.Face)));
            next = page.NextToken;
        } while (!string.IsNullOrEmpty(next));
        return faces;
    }

    public async Task<List<TextSample>> ReadTextAsync(string jobId, CancellationToken ct)
    {
        var texts = new List<TextSample>();
        string? next = null;
        do
        {
            var page = await rekognition.GetTextDetectionAsync(new GetTextDetectionRequest
            {
                JobId = jobId,
                MaxResults = PageSize,
                NextToken = next,
            }, ct);
            texts.AddRange((page.TextDetections ?? [])
                .Where(t => t.TextDetection is { DetectedText.Length: > 0 } && t.TextDetection.Type == TextTypes.LINE)
                .Select(t => new TextSample(t.Timestamp ?? 0, t.TextDetection.DetectedText, t.TextDetection.Confidence ?? 0)));
            next = page.NextToken;
        } while (!string.IsNullOrEmpty(next));
        return texts;
    }

    private static VideoInfo MapVideo(VideoMetadata? m) => new(
        m?.DurationMillis ?? 0,
        Math.Round(m?.FrameRate ?? 0, 2),
        (int)(m?.FrameWidth ?? 0),
        (int)(m?.FrameHeight ?? 0),
        m?.Codec,
        m?.Format);

    private static BoundingBox Map(RekBoundingBox b) =>
        new(b.Left ?? 0, b.Top ?? 0, b.Width ?? 0, b.Height ?? 0);

    private static FaceObservation MapFace(long timestampMs, FaceDetail f) => new(
        timestampMs,
        Map(f.BoundingBox),
        Math.Round(f.Confidence ?? 0, 1),
        f.AgeRange?.Low,
        f.AgeRange?.High,
        f.Gender?.Value?.Value,
        f.Gender?.Confidence is { } gc ? Math.Round(gc, 1) : null,
        f.Emotions?.OrderByDescending(e => e.Confidence).FirstOrDefault()?.Type?.Value,
        f.Eyeglasses?.Value,
        f.Sunglasses?.Value,
        f.Beard?.Value,
        f.Mustache?.Value,
        f.Smile?.Value,
        f.Quality?.Sharpness is { } s ? Math.Round(s, 1) : null,
        f.Pose?.Yaw is { } y ? Math.Round(y, 1) : null);
}
