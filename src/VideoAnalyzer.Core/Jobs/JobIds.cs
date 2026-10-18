using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VideoAnalyzer.Core.Jobs;

/// <summary>
/// Job ids look like "20261018-143012-a1b2c3d4": UTC timestamp first so that S3 lists
/// them in chronological order, plus a random suffix.
/// </summary>
public static partial class JobIds
{
    public static string New(DateTimeOffset now) =>
        $"{now.UtcDateTime:yyyyMMdd-HHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";

    /// <summary>Guards S3 keys built from route values against path tricks like "../".</summary>
    public static bool IsValid(string? jobId) => jobId is not null && Pattern().IsMatch(jobId);

    [GeneratedRegex(@"^\d{8}-\d{6}-[0-9a-f]{8}$")]
    private static partial Regex Pattern();
}
