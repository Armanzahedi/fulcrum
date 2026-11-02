using System.Text.Json.Nodes;
using Fulcrum.OpenAI;
using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests;

public class SchemaBuilderTests
{
    private static readonly JsonObject Schema = SchemaBuilder.Build([TeamQ, SafeQ, UrgencyQ, FaceQ]);

    private static string[] Strings(JsonNode? array) => [.. array!.AsArray().Select(n => n!.GetValue<string>())];

    private static void AssertStrictObject(JsonNode? node, params string[] required)
    {
        Assert.Equal("object", node!["type"]!.GetValue<string>());
        Assert.False(node["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(required, Strings(node["required"]));
        Assert.Equal(required, node["properties"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void Root_requires_every_question_and_forbids_extras()
    {
        AssertStrictObject(Schema, "team", "safe_to_automate", "urgency", "face");
    }

    [Fact]
    public void Choice_schema_has_enum_and_per_option_probabilities()
    {
        var team = Schema["properties"]!["team"];

        AssertStrictObject(team, "choice", "probabilities");
        Assert.Equal("string", team!["properties"]!["choice"]!["type"]!.GetValue<string>());
        Assert.Equal(["Billing", "Technical", "Sales"], Strings(team["properties"]!["choice"]!["enum"]));
        var probabilities = team["properties"]!["probabilities"];
        AssertStrictObject(probabilities, "Billing", "Technical", "Sales");
        Assert.All(probabilities!["properties"]!.AsObject(), p => Assert.Equal("number", p.Value!["type"]!.GetValue<string>()));
    }

    [Fact]
    public void YesNo_schema_has_a_probability()
    {
        var safe = Schema["properties"]!["safe_to_automate"];

        AssertStrictObject(safe, "probability");
        Assert.Equal("number", safe!["properties"]!["probability"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Score_schema_has_integer_level_and_number_array()
    {
        var urgency = Schema["properties"]!["urgency"];

        AssertStrictObject(urgency, "level", "probabilities");
        Assert.Equal("integer", urgency!["properties"]!["level"]!["type"]!.GetValue<string>());
        Assert.Equal("array", urgency["properties"]!["probabilities"]!["type"]!.GetValue<string>());
        Assert.Equal("number", urgency["properties"]!["probabilities"]!["items"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Box_schema_has_boxes_array_with_six_required_numbers()
    {
        var face = Schema["properties"]!["face"];

        AssertStrictObject(face, "boxes");
        var item = face!["properties"]!["boxes"]!["items"];
        AssertStrictObject(item, "image", "x", "y", "w", "h", "confidence");
        Assert.Equal("integer", item!["properties"]!["image"]!["type"]!.GetValue<string>());
        Assert.All(new[] { "x", "y", "w", "h", "confidence" }, p => Assert.Equal("number", item["properties"]![p]!["type"]!.GetValue<string>()));
    }
}
