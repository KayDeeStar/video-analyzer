namespace VideoAnalyzer.Processing.Tests;

public class FailureMessagesTests
{
    [Fact]
    public void Shows_our_own_lambda_error_messages()
    {
        var message = FailureMessages.Describe(
            nameof(UnsupportedVideoFormatException),
            """{"errorType":"UnsupportedVideoFormatException","errorMessage":"AVI videos cannot be analyzed yet."}""");

        Assert.Equal("AVI videos cannot be analyzed yet.", message);
    }

    [Fact]
    public void Explains_rekognition_failures()
    {
        var message = FailureMessages.Describe("Rekognition.LabelDetectionFailed", "Unsupported codec/format.");

        Assert.Equal("The video could not be analyzed: Unsupported codec/format.", message);
    }

    [Fact]
    public void Falls_back_to_a_generic_message()
    {
        Assert.StartsWith("Analysis failed unexpectedly (Lambda.Unknown)", FailureMessages.Describe("Lambda.Unknown", null));
    }
}
