using Fulcrum.OpenAI;
using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum.Tests;

public class UseOpenAITests
{
    [Fact]
    public void UseOpenAI_registers_ISystemOne_as_OpenAIClient()
    {
        using var sp = new ServiceCollection().AddFulcrum(f => f.AddDecision<Triage>().UseOpenAI(o => o.ApiKey = "k")).BuildServiceProvider();

        Assert.IsType<OpenAIClient>(sp.GetRequiredService<ISystemOne>());
    }

    [Fact]
    public void UseOpenAI_without_key_throws_FulcrumException()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        var ex = Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseOpenAI(o => o.ApiKey = null)));

        Assert.Contains("OPENAI_API_KEY", ex.Message);
    }

    [Fact]
    public void UseOpenAI_rejects_MaxAttempts_below_one()
    {
        Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseOpenAI(o => { o.ApiKey = "k"; o.MaxAttempts = 0; })));
    }

    [Fact]
    public void UseOpenAI_does_not_send_OPENAI_API_KEY_to_another_host()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-openai");
        try
        {
            var ex = Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseOpenAI(o =>
            {
                o.ApiKey = "";
                o.BaseAddress = new Uri("https://openrouter.ai/api/v1");
            })));

            Assert.Contains("no API key", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        }
    }

    [Fact]
    public void UseOpenAI_after_another_provider_reports_the_duplicate_provider()
    {
        var ex = Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseFake(new Fulcrum.Testing.FakeSystemOne()).UseOpenAI(_ => { })));

        Assert.Contains("provider is already configured", ex.Message);
    }

    [Theory]
    [InlineData("https://x.example/api?v=1")]
    [InlineData("api/v1")]
    public void UseOpenAI_rejects_base_address_with_query_or_relative(string address)
    {
        Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseOpenAI(o =>
        {
            o.ApiKey = "k";
            o.BaseAddress = new Uri(address, UriKind.RelativeOrAbsolute);
        })));
    }
}
