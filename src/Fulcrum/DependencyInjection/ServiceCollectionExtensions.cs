using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFulcrum(this IServiceCollection services, Action<FulcrumBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(d => d.ServiceType == typeof(FulcrumRegistration)))
            throw new FulcrumException("Fulcrum: AddFulcrum was already called on this service collection; configure every decision and the provider in one call");
        var builder = new FulcrumBuilder(services);
        configure(builder);
        if (!builder.HasProvider)
            throw new FulcrumException("Fulcrum: no provider configured; call UseFake, UseClient, UseJev or UseOpenAI");
        services.AddSingleton<FulcrumRegistration>();
        return services;
    }
}

internal sealed class FulcrumRegistration;
