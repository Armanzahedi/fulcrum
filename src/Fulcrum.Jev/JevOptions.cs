using System.Diagnostics.CodeAnalysis;

namespace Fulcrum.Jev;

[Experimental("FUL0001")]
public sealed class JevOptions
{
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "jev-latest";

    public Uri BaseAddress { get; set; } = new("https://api.typesafe.ai/");
}
