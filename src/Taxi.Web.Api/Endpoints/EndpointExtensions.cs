using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Taxi.Web.Api.Endpoints;

/// <summary>
/// Découverte et enregistrement automatiques des <see cref="IEndpoint"/>.
/// </summary>
public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services)
    {
        var descriptors = Assembly.GetExecutingAssembly().DefinedTypes
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IEndpoint).IsAssignableFrom(t))
            .Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t))
            .ToArray();

        services.TryAddEnumerable(descriptors);
        return services;
    }

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
            endpoint.MapEndpoint(app);
        return app;
    }
}
