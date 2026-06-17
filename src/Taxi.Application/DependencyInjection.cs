using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Application.Abstractions.Behaviors;
using Taxi.Application.Dispatch;
using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(ValidationDecorator.QueryHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));

        services.AddScoped<AuthTokenIssuer>();

        services.AddScoped<IRideDispatcher, RideDispatcher>();

        return services;
    }
}
