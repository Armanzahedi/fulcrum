using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Fulcrum.OpenAI;

internal sealed record ChatRequest(string Model, double Temperature, IReadOnlyList<ChatMessage> Messages, ResponseFormat ResponseFormat);

internal sealed record ChatMessage(string Role, JsonNode Content);

internal sealed record ResponseFormat(string Type, JsonSchemaFormat JsonSchema);

internal sealed record JsonSchemaFormat(string Name, bool Strict, JsonObject Schema);

internal sealed record ChatResponse(List<ChatChoice>? Choices);

internal sealed record ChatChoice(ChatResponseMessage? Message);

internal sealed record ChatResponseMessage(string? Content, string? Refusal);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
internal sealed partial class OpenAIJsonContext : JsonSerializerContext;
