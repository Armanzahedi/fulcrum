
namespace Fulcrum.OpenAI;

public sealed class OpenAIOptions
{
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4.1-mini";

    public Uri BaseAddress { get; set; } = new("https://api.openai.com/v1/");

    public int MaxAttempts { get; set; } = 3;
}
