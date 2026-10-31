using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fulcrum.Jev;

internal sealed record JevRequest(string Model, JevState State, IReadOnlyList<JevQuestion> Questions);

internal sealed record JevState(string Text, IReadOnlyList<JevImage>? Images);

internal sealed record JevImage(string? Data, string? MediaType, string? Url);

internal sealed record JevQuestion(string Name, string Type, string Instruction, IReadOnlyList<JevOption>? Options, IReadOnlyList<string>? Levels);

internal sealed record JevOption(string Name, string Description);

internal sealed record JevResponse(Dictionary<string, JevAnswer?>? Answers);

internal sealed record JevAnswer(string? Type, string? Choice, JsonElement? Probabilities, double? Probability, int? Level, List<JevBox>? Boxes);

internal sealed record JevBox(int? Image, double? X, double? Y, double? W, double? H, double? Confidence);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(JevRequest))]
[JsonSerializable(typeof(JevResponse))]
internal sealed partial class JevJsonContext : JsonSerializerContext;
