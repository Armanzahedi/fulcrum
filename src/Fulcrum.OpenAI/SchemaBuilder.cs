using System.Text.Json.Nodes;

namespace Fulcrum.OpenAI;

internal static class SchemaBuilder
{
    public static JsonObject Build(IReadOnlyList<Question> questions) =>
        StrictObject(questions.Select(q => (q.Name, (JsonNode)ForQuestion(q))));

    private static JsonObject ForQuestion(Question q) => q.Kind switch
    {
        QuestionKind.Choice => StrictObject(
            ("choice", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. q.Choices.Select(c => (JsonNode)c.Name)]) }),
            ("probabilities", StrictObject(q.Choices.Select(c => (c.Name, Type("number")))))),
        QuestionKind.YesNo => StrictObject(("probability", Type("number"))),
        QuestionKind.Score => StrictObject(
            ("level", Type("integer")),
            ("probabilities", new JsonObject { ["type"] = "array", ["items"] = Type("number") })),
        _ => StrictObject(("boxes", new JsonObject
        {
            ["type"] = "array",
            ["items"] = StrictObject(
                ("image", Type("integer")),
                ("x", Type("number")),
                ("y", Type("number")),
                ("w", Type("number")),
                ("h", Type("number")),
                ("confidence", Type("number"))),
        })),
    };

    private static JsonNode Type(string type) => new JsonObject { ["type"] = type };

    private static JsonObject StrictObject(params IEnumerable<(string Name, JsonNode Schema)> properties)
    {
        var list = properties.ToList();
        var props = new JsonObject();
        foreach (var (name, schema) in list)
            props[name] = schema;
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray([.. list.Select(p => (JsonNode)p.Name)]),
            ["properties"] = props,
        };
    }
}
