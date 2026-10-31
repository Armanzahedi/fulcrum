using System.Diagnostics.CodeAnalysis;
using Fulcrum.Jev;
using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum;

[Experimental("FUL0001")]
public static class JevFulcrumBuilderExtensions
{
    public static FulcrumBuilder UseJev(this FulcrumBuilder f, Action<JevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new JevOptions();
        configure(options);
        f.UseClient(sp => new JevClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Fulcrum.Jev"), options));
        options.ApiKey = Endpoint.ApiKey(options.ApiKey, options.BaseAddress, "api.typesafe.ai", "JEV_API_KEY");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new FulcrumException("Jev: no API key; set JevOptions.ApiKey or the JEV_API_KEY environment variable (read only with the default BaseAddress)");

        Endpoint.Resolve("Jev", options.BaseAddress, "");
        f.Services.AddHttpClient("Fulcrum.Jev")
            .AddHttpMessageHandler(sp => new RetryingHandler(sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return f;
    }
}
