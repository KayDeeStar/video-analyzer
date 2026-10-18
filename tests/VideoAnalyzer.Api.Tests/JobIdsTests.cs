using VideoAnalyzer.Core.Jobs;

namespace VideoAnalyzer.Api.Tests;

public class JobIdsTests
{
    [Fact]
    public void New_ids_are_valid_and_start_with_the_utc_timestamp()
    {
        var id = JobIds.New(new DateTimeOffset(2026, 10, 18, 14, 30, 12, TimeSpan.FromHours(5)));

        Assert.StartsWith("20261018-093012-", id);
        Assert.True(JobIds.IsValid(id));
    }

    [Fact]
    public void New_ids_are_unique()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.NotEqual(JobIds.New(now), JobIds.New(now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../uploads")]
    [InlineData("20261018-093012-a1b2c3d4/../x")]
    [InlineData("20261018-093012-A1B2C3D4")]
    public void Rejects_malformed_ids(string? id)
    {
        Assert.False(JobIds.IsValid(id));
    }
}
