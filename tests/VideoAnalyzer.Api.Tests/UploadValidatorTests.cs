using VideoAnalyzer.Api.Uploads;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Api.Tests;

public class UploadValidatorTests
{
    private const long MaxBytes = 2048L * 1024 * 1024;
    private const long Mb = 1024 * 1024;

    [Theory]
    [InlineData("camera1.mp4")]
    [InlineData("LOBBY.MOV")]
    [InlineData(@"C:\exports\gate.avi")]
    public void Accepts_supported_videos(string fileName)
    {
        var errors = UploadValidator.Validate(new CreateUploadRequest(fileName, 10 * Mb), MaxBytes);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notes.txt")]
    [InlineData("footage")]
    public void Rejects_bad_file_names(string fileName)
    {
        var errors = UploadValidator.Validate(new CreateUploadRequest(fileName, 10 * Mb), MaxBytes);

        Assert.Contains(nameof(CreateUploadRequest.FileName), errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MaxBytes + 1)]
    public void Rejects_bad_sizes(long size)
    {
        var errors = UploadValidator.Validate(new CreateUploadRequest("a.mp4", size), MaxBytes);

        Assert.Contains(nameof(CreateUploadRequest.FileSize), errors.Keys);
    }

    [Fact]
    public void Rejects_missing_body()
    {
        Assert.NotEmpty(UploadValidator.Validate(null, MaxBytes));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(16 * Mb, 1)]
    [InlineData(16 * Mb + 1, 2)]
    [InlineData(2048 * Mb, 128)]
    public void Splits_file_into_parts(long fileSize, int expectedParts)
    {
        var partSize = UploadValidator.PartSizeFor(fileSize, 16 * Mb);

        Assert.Equal(16 * Mb, partSize);
        Assert.Equal(expectedParts, UploadValidator.PartCountFor(fileSize, partSize));
    }

    [Fact]
    public void Grows_part_size_to_stay_within_10000_parts()
    {
        const long fileSize = 200L * 1024 * Mb; // 200 GB
        var partSize = UploadValidator.PartSizeFor(fileSize, 16 * Mb);

        Assert.True(UploadValidator.PartCountFor(fileSize, partSize) <= 10_000);
    }
}
