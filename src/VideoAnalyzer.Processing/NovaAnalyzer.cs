using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing;

/// <summary>
/// Asks an Amazon Nova model (Bedrock Converse API) to watch the video straight from S3 and write
/// an investigator-style report as JSON.
/// </summary>
public sealed class NovaAnalyzer(IAmazonBedrockRuntime bedrock, string modelId)
{
    public string ModelId => modelId;

    public const string SystemPrompt =
        """
        You are an expert forensic video analyst helping investigators review security camera footage.
        Describe only what is actually visible. Be specific and factual: colors, garment types, materials,
        positions and directions of movement. When something cannot be determined from the footage, use null
        instead of guessing. Never identify anyone by name and never state ethnicity or nationality; describe
        apparent skin tone instead. Estimates of age, height and build are allowed but must be approximate ranges.
        """;

    public const string Instructions =
        """
        Analyze this security camera video. Respond with ONLY a JSON object (no markdown, no commentary) in exactly this shape:

        {
          "summary": "2-4 sentences: who does what, where, and anything an investigator should notice",
          "narrative": [
            { "title": "Individual appearance", "text": "..." },
            { "title": "Action and behavior", "text": "..." },
            { "title": "Location details", "text": "..." },
            { "title": "Observable physical details", "text": "..." },
            { "title": "Limitations", "text": "..." }
          ],
          "setting": "type of place, indoor/outdoor, lighting, camera position",
          "visibleText": ["exact text of signs, logos, labels, licence plates"],
          "onScreenDateTime": "date/time overlay drawn by the camera, copied exactly, or null",
          "people": [
            {
              "label": "Person 1",
              "firstSeenSeconds": 0.0,
              "lastSeenSeconds": 0.0,
              "apparentSex": "male / female / unclear",
              "apparentAge": "e.g. 16-22",
              "height": "approximate, relative to nearby objects if possible",
              "build": "e.g. slim / average / heavy",
              "skinTone": "apparent skin tone, e.g. light / medium / dark",
              "hair": "color, length, style",
              "facialHair": "description or null",
              "headwear": "description with color or null",
              "upperClothing": "garment type, color, details",
              "lowerClothing": "garment type, color, details",
              "footwear": "type and color",
              "accessories": "glasses, gloves, mask, jewelry, bags worn",
              "carriedItems": "objects held or carried",
              "distinguishingFeatures": "tattoos, logos on clothing, injuries, gait, anything unusual",
              "actions": "what this person does, in order",
              "faceVisibility": "clear / partial / not visible"
            }
          ],
          "vehicles": [
            { "type": "car / van / truck / motorcycle / bicycle", "color": "", "makeModel": "or null",
              "licensePlate": "exact characters or null", "firstSeenSeconds": 0.0, "actions": "" }
          ],
          "timeline": [ { "seconds": 0.0, "description": "what happens" } ],
          "notableEvents": [ { "seconds": 0.0, "description": "", "severity": "low / medium / high" } ],
          "qualityNotes": "lighting, blur, obstruction or resolution problems that limit what can be seen"
        }

        Rules:
        - narrative is a written briefing for an investigator in plain, natural English: complete sentences, no bullet
          fragments, specific and concrete. Use exactly these sections, in this order:
            "Individual appearance": each person's hair (color, length, highlights), clothing layers with colors, and
              anything they hold. With several people, describe each one, introducing them as Person 1, Person 2, ...
            "Action and behavior": what each person does, step by step, with approximate times such as "at about 0:04".
            "Location details": the setting, background features (walls, murals, poles, vehicles), and the exact wording
              of any signs, in quotation marks.
            "Observable physical details": apparent age range, hair, apparent skin tone, eyes and facial features that
              are visible, and from which angles (profile, three-quarter, frontal).
            "Limitations": what cannot be determined from the footage (exact age, identity, ethnic background) and
              anything that limits the view (blur, lighting, obstruction).
        - Times are seconds from the start of the video.
        - List each distinct person once, even if they appear several times, labelled "Person 1", "Person 2", ... in order of first appearance.
        - timeline: 3 to 15 entries describing what happens, in order.
        - notableEvents: anything an investigator would care about, such as crimes, vandalism, damage, covering or tampering
          with the camera, weapons, fights, theft, trespassing or suspicious behaviour. Use an empty list if there are none.
        - Use empty lists when there are no people or vehicles.
        """;

    private static readonly Dictionary<string, string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "mp4", [".m4v"] = "mp4", [".mov"] = "mov", [".mkv"] = "mkv", [".webm"] = "webm",
        [".flv"] = "flv", [".mpeg"] = "mpeg", [".mpg"] = "mpg", [".wmv"] = "wmv", [".3gp"] = "three_gp",
    };

    public static bool Supports(string key) => Formats.ContainsKey(Path.GetExtension(key));

    public async Task<(AiReport Report, int InputTokens, int OutputTokens)> DescribeAsync(
        string bucket, string key, CancellationToken ct)
    {
        var format = Formats.TryGetValue(Path.GetExtension(key), out var f)
            ? f
            : throw new UnsupportedVideoFormatException($"{Path.GetExtension(key)} videos cannot be described.");

        var response = await bedrock.ConverseAsync(new ConverseRequest
        {
            ModelId = modelId,
            System = [new SystemContentBlock { Text = SystemPrompt }],
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content =
                    [
                        new ContentBlock
                        {
                            Video = new VideoBlock
                            {
                                Format = VideoFormat.FindValue(format),
                                Source = new VideoSource { S3Location = new S3Location { Uri = $"s3://{bucket}/{key}" } },
                            },
                        },
                        new ContentBlock { Text = Instructions },
                    ],
                },
            ],
            InferenceConfig = new InferenceConfiguration { MaxTokens = 4096, Temperature = 0 },
        }, ct);

        var text = string.Concat((response.Output?.Message?.Content ?? []).Select(c => c.Text));
        var report = ReportParser.Parse(text);
        return (report, response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0);
    }
}
