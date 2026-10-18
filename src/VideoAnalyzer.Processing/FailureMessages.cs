using System.Text.Json;

namespace VideoAnalyzer.Processing;

/// <summary>Turns Step Functions error/cause pairs into short messages shown on the job page.</summary>
public static class FailureMessages
{
    public static string Describe(string? error, string? cause)
    {
        switch (error)
        {
            case nameof(UnsupportedVideoFormatException):
            case nameof(InvalidJobException):
                return LambdaErrorMessage(cause) ?? "The video could not be analyzed.";
            case "States.Timeout":
                return "Analysis took too long and was stopped.";
            case not null when error.StartsWith("Rekognition.", StringComparison.Ordinal):
                return string.IsNullOrWhiteSpace(cause)
                    ? "The video could not be analyzed. Make sure it is an H.264 MP4 or MOV file."
                    : $"The video could not be analyzed: {Trim(cause)}";
            default:
                return $"Analysis failed unexpectedly ({error ?? "unknown error"}). Please try uploading again.";
        }
    }

    /// <summary>Lambda failures arrive as {"errorType": "...", "errorMessage": "..."} in the cause.</summary>
    private static string? LambdaErrorMessage(string? cause)
    {
        if (string.IsNullOrWhiteSpace(cause))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(cause);
            return doc.RootElement.TryGetProperty("errorMessage", out var message) ? message.GetString() : null;
        }
        catch (JsonException)
        {
            return Trim(cause);
        }
    }

    private static string Trim(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
