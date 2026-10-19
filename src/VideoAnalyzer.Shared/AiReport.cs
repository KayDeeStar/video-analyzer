namespace VideoAnalyzer.Shared;

// Investigator-style description of a video written by a vision model (Amazon Nova via Bedrock).
// Times are seconds from the start of the video (approximate: the model samples ~1 frame/second).
// Every attribute is an apparent, unverified estimate; null means "could not be determined".

/// <param name="Narrative">The analyst-style written briefing, in plain language, one section per topic.</param>
public sealed record AiReport(
    string Summary,
    IReadOnlyList<ReportSection> Narrative,
    string? Setting,
    IReadOnlyList<string> VisibleText,
    string? OnScreenDateTime,
    IReadOnlyList<PersonDescription> People,
    IReadOnlyList<VehicleDescription> Vehicles,
    IReadOnlyList<AnimalDescription> Animals,
    IReadOnlyList<TimelineEvent> Timeline,
    IReadOnlyList<TimelineEvent> NotableEvents,
    string? QualityNotes);

/// <summary>One titled part of the written briefing, e.g. "Action and behavior".</summary>
public sealed record ReportSection(string Title, string Text);

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

/// <param name="Species">As specific as the footage allows, e.g. "gray fox", "striped skunk", or "small mammal".</param>
/// <param name="Count">Most individuals of this species visible at the same time.</param>
public sealed record AnimalDescription(
    string Species,
    int? Count,
    double? FirstSeenSeconds,
    double? LastSeenSeconds,
    string? Appearance,
    string? Behavior);

/// <param name="Severity">For notable events: low, medium or high.</param>
public sealed record TimelineEvent(double Seconds, string Description, string? Severity);
