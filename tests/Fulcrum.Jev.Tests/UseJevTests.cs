using Fulcrum.Jev;
using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum.Tests;

public class UseJevTests
{
    [Fact]
    public void UseJev_registers_ISystemOne_as_JevClient()
    {
        using var sp = new ServiceCollection().AddFulcrum(f => f.AddDecision<Triage>().UseJev(o => o.ApiKey = "k")).BuildServiceProvider();

        Assert.IsType<JevClient>(sp.GetRequiredService<ISystemOne>());
    }

    [Fact]
    public void UseJev_without_api_key_or_env_var_throws_FulcrumException()
    {
        Environment.SetEnvironmentVariable("JEV_API_KEY", null);

        var ex = Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseJev(o => o.ApiKey = null)));

        Assert.Contains("JEV_API_KEY", ex.Message);
    }

    [Fact]
    public void UseJev_reads_JEV_API_KEY_env_var()
    {
        Environment.SetEnvironmentVariable("JEV_API_KEY", "from-env");
        try
        {
            using var sp = new ServiceCollection().AddFulcrum(f => f.UseJev(_ => { })).BuildServiceProvider();

            Assert.IsType<JevClient>(sp.GetRequiredService<ISystemOne>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JEV_API_KEY", null);
        }
    }

    [Fact]
    public void UseJev_rejects_base_address_with_query()
    {
        Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseJev(o =>
        {
            o.ApiKey = "k";
            o.BaseAddress = new Uri("https://x.example/api?v=1");
        })));
    }
}
