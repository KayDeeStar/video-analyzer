using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing.Tests;

public class ResultBuilderTests
{
    private static readonly BoundingBox Box = new(0.1, 0.1, 0.2, 0.4);

    private static LabelSample Person(long ms, int count = 1) =>
        new(ms, "Person", 99, ["Person Description"], Enumerable.Repeat(Box, count).ToList());

    private static FaceObservation Face(long ms, BoundingBox box, double sharpness = 80, double yaw = 0) =>
        new(ms, box, 99, 25, 35, "Male", 98, "CALM", false, false, true, false, false, sharpness, yaw);

    [Fact]
    public void Merges_close_person_detections_into_appearances()
    {
        var ranges = ResultBuilder.MergeIntoRanges([0, 500, 1000, 1500, 9000, 9500, 30_000], maxGapMs: 2000);

        Assert.Equal(
            [new TimeRange(0, 1500), new TimeRange(9000, 9500), new TimeRange(30_000, 30_000)],
            ranges);
    }

    [Fact]
    public void Merging_no_detections_gives_no_appearances()
    {
        Assert.Empty(ResultBuilder.MergeIntoRanges([], 2000));
    }

    [Fact]
    public void Counts_most_people_visible_at_once()
    {
        var result = Build(labels: [Person(0, 1), Person(500, 3), Person(1000, 2)]);

        Assert.Equal(3, result.People.MaxVisibleAtOnce);
        Assert.Single(result.People.Appearances);
    }

    [Fact]
    public void Skips_duplicate_faces_of_the_same_moment_and_place()
    {
        var faces = new[]
        {
            Face(1000, Box, sharpness: 50),
            Face(1500, Box with { Left = 0.11 }, sharpness: 90), // same face half a second later, sharper
            Face(1500, new BoundingBox(0.7, 0.1, 0.2, 0.4)),       // a second person elsewhere in the frame
            Face(60_000, Box),                                     // same spot a minute later
        };

        var picked = ResultBuilder.SelectBestFaces(faces, max: 10);

        Assert.Equal(3, picked.Count);
        Assert.Contains(picked, f => f.TimestampMs == 1500 && f.Sharpness == 90);
        Assert.DoesNotContain(picked, f => f.Sharpness == 50);
        Assert.Equal(picked.OrderBy(f => f.TimestampMs), picked); // video order
    }

    [Fact]
    public void Prefers_frontal_faces()
    {
        var faces = new[] { Face(0, Box, yaw: 80), Face(100, Box, yaw: 5) };

        var picked = ResultBuilder.SelectBestFaces(faces, max: 1);

        Assert.Equal(5, picked[0].Yaw);
    }

    [Fact]
    public void Summarizes_labels_by_name()
    {
        var labels = new[]
        {
            new LabelSample(0, "Car", 80, ["Vehicles and Automotive"], [Box]),
            new LabelSample(4000, "Car", 95, ["Vehicles and Automotive"], [Box, Box]),
            new LabelSample(2000, "Backpack", 70, ["Accessories"], []),
        };

        var summary = ResultBuilder.SummarizeLabels(labels);

        var car = Assert.Single(summary, l => l.Name == "Car");
        Assert.Equal((2, 95.0, 2, 0L, 4000L), (car.Detections, car.MaxConfidence, car.MaxInstances, car.FirstSeenMs, car.LastSeenMs));
        Assert.Equal("Car", summary[0].Name); // most detections first
    }

    [Theory]
    [InlineData("2024-03-05 14:22:31", true)]
    [InlineData("03/05/2024", true)]
    [InlineData("14:22:31", true)]
    [InlineData("9:05 PM", true)]
    [InlineData("CAM 01", false)]
    [InlineData("EXIT", false)]
    [InlineData("ABC 1234", false)]
    public void Recognizes_camera_clock_overlays(string text, bool expected)
    {
        Assert.Equal(expected, ResultBuilder.IsClockText(text));
    }

    [Fact]
    public void Reports_first_and_last_clock_reading_and_keeps_other_text()
    {
        var texts = new[]
        {
            new TextSample(0, "2024-03-05", 99),
            new TextSample(0, "14:22:31", 99),
            new TextSample(1000, "2024-03-05", 99),
            new TextSample(1000, "14:22:32", 99),
            new TextSample(500, "CAM 01", 95),
            new TextSample(1500, "cam 01", 97),
        };

        var result = Build(texts: texts);

        Assert.Equal(new OnScreenClock("2024-03-05 14:22:31", 0, "2024-03-05 14:22:32", 1000), result.OnScreenClock);
        var text = Assert.Single(result.Texts);
        Assert.Equal((2, 97.0, 500L, 1500L), (text.Detections, text.Confidence, text.FirstSeenMs, text.LastSeenMs));
    }

    [Fact]
    public void Handles_video_without_any_detections()
    {
        var result = Build();

        Assert.Equal(0, result.People.MaxVisibleAtOnce);
        Assert.Empty(result.Faces);
        Assert.Null(result.OnScreenClock);
    }

    private static AnalysisResult Build(
        LabelSample[]? labels = null, FaceObservation[]? faces = null, TextSample[]? texts = null) =>
        ResultBuilder.Build("20261018-000000-00000000", DateTimeOffset.UnixEpoch,
            new VideoInfo(60_000, 25, 1920, 1080, "h264", "QuickTime / MOV"),
            labels ?? [], faces ?? [], texts ?? []);
}
