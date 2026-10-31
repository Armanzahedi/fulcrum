using Fulcrum.OpenAI;
using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum;

public static class OpenAIFulcrumBuilderExtensions
{
    public static FulcrumBuilder UseOpenAI(this FulcrumBuilder f, Action<OpenAIOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new OpenAIOptions();
        configure(options);
        f.UseClient(sp => new OpenAIClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Fulcrum.OpenAI"), options));
        options.ApiKey = Endpoint.ApiKey(options.ApiKey, options.BaseAddress, "api.openai.com", "OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new FulcrumException("OpenAI: no API key; set OpenAIOptions.ApiKey or the OPENAI_API_KEY environment variable (read only with the default BaseAddress)");
        if (options.MaxAttempts < 1)
            throw new FulcrumException("OpenAI: MaxAttempts must be at least 1");

        Endpoint.Resolve("OpenAI", options.BaseAddress, "");
        f.Services.AddHttpClient("Fulcrum.OpenAI")
            .AddHttpMessageHandler(sp => new RetryingHandler(sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return f;
    }
}
