namespace VideoAnalyzer.Shared;

// Investigator-style description of a video written by a vision model (Amazon Nova via Bedrock).
// Times are seconds from the start of the video (approximate: the model samples ~1 frame/second).
// Every attribute is an apparent, unverified estimate; null means "could not be determined".

public sealed record AiReport(
    string Summary,
    string? Setting,
    IReadOnlyList<string> VisibleText,
    string? OnScreenDateTime,
    IReadOnlyList<PersonDescription> People,
    IReadOnlyList<VehicleDescription> Vehicles,
    IReadOnlyList<TimelineEvent> Timeline,
    IReadOnlyList<TimelineEvent> NotableEvents,
    string? QualityNotes);

public sealed record PersonDescription(
    string Label,
    double? FirstSeenSeconds,
    double? LastSeenSeconds,
    string? ApparentSex,
    string? ApparentAge,
    string? Height,
    string? Build,
    string? SkinTone,
    string? Hair,
    string? FacialHair,
    string? Headwear,
    string? UpperClothing,
    string? LowerClothing,
    string? Footwear,
    string? Accessories,
    string? CarriedItems,
    string? DistinguishingFeatures,
    string? Actions,
    string? FaceVisibility);

public sealed record VehicleDescription(
    string? Type,
    string? Color,
    string? MakeModel,
    string? LicensePlate,
    double? FirstSeenSeconds,
    string? Actions);

/// <param name="Severity">For notable events: low, medium or high.</param>
public sealed record TimelineEvent(double Seconds, string Description, string? Severity);
