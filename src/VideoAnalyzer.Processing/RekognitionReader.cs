using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using VideoAnalyzer.Shared;
using RekBoundingBox = Amazon.Rekognition.Model.BoundingBox;
using BoundingBox = VideoAnalyzer.Shared.BoundingBox;

namespace VideoAnalyzer.Processing;

/// <summary>Reads every page of a finished Rekognition Video label detection job.</summary>
public sealed class RekognitionReader(IAmazonRekognition rekognition)
{
    private const int PageSize = 1000;

    /// <summary>Instances (e.g. individual people) below this confidence are ignored when counting.</summary>
    public const float MinInstanceConfidence = 70;

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
                    (d.Label.Instances ?? [])
                        .Where(i => i.BoundingBox is not null && (i.Confidence ?? 0) >= MinInstanceConfidence)
                        .Select(i => Map(i.BoundingBox))
                        .ToList())));
            next = page.NextToken;
        } while (!string.IsNullOrEmpty(next));

        return (MapVideo(metadata), labels);
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
}
