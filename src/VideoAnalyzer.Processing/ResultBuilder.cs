using System.Text.RegularExpressions;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing;

/// <summary>Turns thousands of per-moment Rekognition detections into a readable summary.</summary>
public static partial class ResultBuilder
{
    public const string PersonLabel = "Person";

    /// <summary>Person detections closer together than this are treated as one continuous appearance.</summary>
    public const long AppearanceGapMs = 2_000;

    public const int MaxFaces = 30;
    public const int MaxLabels = 60;
    public const int MaxTexts = 50;

    public static AnalysisResult Build(
        string jobId,
        DateTimeOffset analyzedAt,
        VideoInfo video,
        IReadOnlyList<LabelSample> labels,
        IReadOnlyList<FaceObservation> faces,
        IReadOnlyList<TextSample> texts)
    {
        var persons = labels.Where(l => l.Name == PersonLabel).ToList();
        var (clock, otherTexts) = SplitClockText(texts);

        return new AnalysisResult(
            jobId,
            analyzedAt,
            video,
            new PeopleSummary(
                MaxVisibleAtOnce: persons.Count == 0 ? 0 : persons.Max(p => Math.Max(1, p.Boxes.Count)),
                Appearances: MergeIntoRanges(persons.Select(p => p.TimestampMs), AppearanceGapMs)),
            SelectBestFaces(faces, MaxFaces),
            faces.Count,
            SummarizeLabels(labels),
            SummarizeTexts(otherTexts),
            clock);
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

    /// <summary>
    /// Picks the clearest, most frontal faces, skipping near-duplicates (same place in the frame
    /// within a couple of seconds), and returns them in video order.
    /// </summary>
    public static IReadOnlyList<FaceObservation> SelectBestFaces(IReadOnlyList<FaceObservation> faces, int max)
    {
        var picked = new List<FaceObservation>();
        foreach (var face in faces.OrderByDescending(Quality))
        {
            if (picked.Count >= max)
                break;
            var duplicate = picked.Any(p =>
                Math.Abs(p.TimestampMs - face.TimestampMs) <= AppearanceGapMs && IoU(p.Box, face.Box) > 0.3);
            if (!duplicate)
                picked.Add(face);
        }
        return picked.OrderBy(f => f.TimestampMs).ToList();
    }

    private static double Quality(FaceObservation face)
    {
        var sharpness = face.Sharpness ?? 50;
        var frontal = 1 - Math.Min(Math.Abs(face.Yaw ?? 0), 90) / 90;
        var size = Math.Sqrt(face.Box.Width * face.Box.Height); // bigger faces carry more detail
        return face.Confidence / 100 * sharpness * frontal * (0.5 + size);
    }

    /// <summary>Intersection over union of two boxes (0 = no overlap, 1 = identical).</summary>
    public static double IoU(BoundingBox a, BoundingBox b)
    {
        var x1 = Math.Max(a.Left, b.Left);
        var y1 = Math.Max(a.Top, b.Top);
        var x2 = Math.Min(a.Left + a.Width, b.Left + b.Width);
        var y2 = Math.Min(a.Top + a.Height, b.Top + b.Height);
        var intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var union = a.Width * a.Height + b.Width * b.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
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
            .OrderByDescending(l => l.Detections)
            .ThenBy(l => l.Name)
            .Take(MaxLabels)
            .ToList();

    public static IReadOnlyList<TextSummary> SummarizeTexts(IEnumerable<TextSample> texts) =>
        texts
            .Select(t => t with { Text = Whitespace().Replace(t.Text, " ").Trim() })
            .Where(t => t.Text.Length >= 2)
            .GroupBy(t => t.Text, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TextSummary(
                g.First().Text,
                Math.Round(g.Max(t => t.Confidence), 1),
                g.Count(),
                g.Min(t => t.TimestampMs),
                g.Max(t => t.TimestampMs)))
            .OrderByDescending(t => t.Detections)
            .ThenBy(t => t.FirstSeenMs)
            .Take(MaxTexts)
            .ToList();

    /// <summary>
    /// CCTV recorders burn a date/time overlay into the image. Those lines change every second,
    /// so they are reported as the first and last clock reading instead of hundreds of text rows.
    /// </summary>
    public static (OnScreenClock? Clock, IReadOnlyList<TextSample> Others) SplitClockText(IReadOnlyList<TextSample> texts)
    {
        var clockLines = texts.Where(t => IsClockText(t.Text)).ToList();
        var others = texts.Except(clockLines).ToList();
        if (clockLines.Count == 0)
            return (null, others);

        var byMoment = clockLines
            .GroupBy(t => t.TimestampMs)
            .OrderBy(g => g.Key)
            .Select(g => (Ms: g.Key, Text: string.Join(" ", g.Select(t => t.Text.Trim()).Distinct())))
            .ToList();

        var first = byMoment[0];
        var last = byMoment[^1];
        return (new OnScreenClock(first.Text, first.Ms, last.Text, last.Ms), others);
    }

    public static bool IsClockText(string text) => TimeOfDay().IsMatch(text) || CalendarDate().IsMatch(text);

    [GeneratedRegex(@"\b([01]?\d|2[0-3])[:.][0-5]\d([:.][0-5]\d)?\b")]
    private static partial Regex TimeOfDay();

    [GeneratedRegex(@"\b(\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4})\b")]
    private static partial Regex CalendarDate();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
