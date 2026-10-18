using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Api.Uploads;

public static class UploadValidator
{
    public const int MaxFileNameLength = 255;

    /// <summary>Returns validation errors keyed by field name (empty when valid).</summary>
    public static Dictionary<string, string[]> Validate(CreateUploadRequest? request, long maxUploadBytes)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["Request body is required."];
            return errors;
        }

        var fileName = Path.GetFileName(request.FileName ?? "").Trim();
        if (fileName.Length == 0)
            errors[nameof(request.FileName)] = ["File name is required."];
        else if (fileName.Length > MaxFileNameLength)
            errors[nameof(request.FileName)] = [$"File name must be at most {MaxFileNameLength} characters."];
        else if (!VideoFormats.IsAllowed(fileName))
            errors[nameof(request.FileName)] =
                [$"Unsupported file type. Allowed: {string.Join(", ", VideoFormats.AllowedExtensions.Order())}."];

        if (request.FileSize <= 0)
            errors[nameof(request.FileSize)] = ["File size must be greater than zero."];
        else if (request.FileSize > maxUploadBytes)
            errors[nameof(request.FileSize)] = [$"File is too large. Maximum is {maxUploadBytes / (1024 * 1024)} MB."];

        return errors;
    }

    /// <summary>Part size to use: the configured size, grown if needed to stay within S3's 10,000-part limit.</summary>
    public static long PartSizeFor(long fileSize, long configuredPartSize)
    {
        const int maxParts = 10_000;
        var minimum = (fileSize + maxParts - 1) / maxParts;
        return Math.Max(configuredPartSize, minimum);
    }

    public static int PartCountFor(long fileSize, long partSize) => (int)((fileSize + partSize - 1) / partSize);
}
