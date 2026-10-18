using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing;

/// <summary>Combines the vision model's report with a readable summary of Rekognition's per-moment detections.</summary>
public static class ResultBuilder
{
    public const string PersonLabel = "Person";

    /// <summary>Person detections closer together than this are treated as one continuous appearance.</summary>
    public const long AppearanceGapMs = 2_000;

    public const int MaxLabels = 60;

    /// <summary>Labels seen only once or twice below this confidence are mostly noise (e.g. a spray can read as "Gun").</summary>
    public const double MinConfidence = 80;

    public const int MinDetectionsWhenUnsure = 3;

    public static AnalysisResult Build(
        string jobId,
        DateTimeOffset analyzedAt,
        VideoInfo video,
        IReadOnlyList<LabelSample> labels,
        AiReport? report,
        string? reportModel)
    {
        var persons = labels.Where(l => l.Name == PersonLabel).ToList();

        return new AnalysisResult(
            jobId,
            analyzedAt,
            video,
            report,
            reportModel,
            new PeopleSummary(
                MaxVisibleAtOnce: persons.Count == 0 ? 0 : persons.Max(p => Math.Max(1, p.Boxes.Count)),
                Appearances: MergeIntoRanges(persons.Select(p => p.TimestampMs), AppearanceGapMs)),
            SummarizeLabels(labels));
    }

    /// <summary>Groups sorted timestamps into ranges, starting a new range after a gap larger than <paramref name="maxGapMs"/>.</summary>
    public static IReadOnlyList<TimeRange> MergeIntoRanges(IEnumerable<long> timestamps, long maxGapMs)
    {
        var ranges = new List<TimeRange>();
        long? start = null, end = null;
        foreach (var t in timestamps.Order())
        {
            if (start is null)
            {
                start = end = t;
            }
            else if (t - end > maxGapMs)
            {
                ranges.Add(new TimeRange(start.Value, end!.Value));
                start = end = t;
            }
            else
            {
                end = t;
            }
        }
        if (start is not null)
            ranges.Add(new TimeRange(start.Value, end!.Value));
        return ranges;
    }

    public static IReadOnlyList<LabelSummary> SummarizeLabels(IReadOnlyList<LabelSample> labels) =>
        labels
            .GroupBy(l => l.Name)
            .Select(g => new LabelSummary(
                g.Key,
                g.SelectMany(l => l.Categories).Distinct().Order().ToList(),
                Math.Round(g.Max(l => l.Confidence), 1),
                g.Count(),
                g.Max(l => l.Boxes.Count),
                g.Min(l => l.TimestampMs),
                g.Max(l => l.TimestampMs)))
            .Where(l => l.MaxConfidence >= MinConfidence || l.Detections >= MinDetectionsWhenUnsure)
            .OrderByDescending(l => l.Detections)
            .ThenBy(l => l.Name)
            .Take(MaxLabels)
            .ToList();
}
