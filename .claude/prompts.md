# Prompts réutilisables — TaxiDjibouti

> Tous les exemples suivent les patterns du projet : **CQRS sans MediatR**, Result pattern, `IEndpoint` + `ToHttpResult()`, Ardalis.Specification, logging source-generated. **Jamais** de MediatR, `result.Match`, `ApiResults.Problem`, Serilog ni soft-delete.

## Ajouter une commande (écriture)

```csharp
// 1. La commande (record) — Taxi.Application/{Module}/{Action}/
public sealed record RequestRideCommand(string ClientId, string PickupZone, string DestinationZone)
    : ICommand<RideDto>;

// 2. Le handler (internal sealed) — enregistré automatiquement par Scrutor
internal sealed class RequestRideCommandHandler(
    IRepository<Ride> rides,
    IQueryHandler<EstimatePriceQuery, EstimatePriceResponse> pricing)
    : ICommandHandler<RequestRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(RequestRideCommand command, CancellationToken cancellationToken)
    {
        var price = await pricing.Handle(new EstimatePriceQuery(command.PickupZone, command.DestinationZone), cancellationToken);
        if (price.IsFailure) return Result.Failure<RideDto>(price.Error);

        var ride = Ride.Request(command.ClientId, /* ... */ price.Value.Price);
        await rides.AddAsync(ride, cancellationToken);
        return RideDto.From(ride);
    }
}

// 3. (Optionnel) Le validateur — appliqué automatiquement par le décorateur
public sealed class RequestRideCommandValidator : AbstractValidator<RequestRideCommand>
{
    public RequestRideCommandValidator()
    {
        RuleFor(x => x.PickupZone).NotEmpty().WithMessage("La zone de départ est requise.");
    }
}
```

## Ajouter une requête (lecture)

```csharp
public sealed record GetMyDriverQuery(string UserId) : IQuery<DriverDto>;

internal sealed class GetMyDriverQueryHandler(IRepository<Driver> drivers)
    : IQueryHandler<GetMyDriverQuery, DriverDto>
{
    public async Task<Result<DriverDto>> Handle(GetMyDriverQuery query, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(query.UserId), cancellationToken);
        return driver is null
            ? Result.Failure<DriverDto>(DriverErrors.NotFound)
            : DriverDto.From(driver);
    }
}
```

## Ajouter une spécification (Ardalis)

```csharp
// Taxi.Application/{Module}/...Specs.cs  (interne)
internal sealed class RideByIdSpec : Specification<Ride>
{
    public RideByIdSpec(int rideId) => Query.Where(r => r.Id == rideId);
}
```

## Ajouter un endpoint

```csharp
public sealed class RequestRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides", async (
            RequestRideRequest body,
            ClaimsPrincipal principal,
            ICommandHandler<RequestRideCommand, RideDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new RequestRideCommand(userId, body.PickupZone, body.DestinationZone), ct);
            return result.ToHttpResult();            // jamais result.Match / ApiResults.Problem
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("RequestRide")
        .WithTags(Tags.Rides)
        .WithSummary("Demander une course");
    }
}
```

## Ajouter une entité du domaine

1. **Domain** : classe héritant de `Entity` (`int Id`, `CreatedAt`), constructeur privé pour EF, **factory** `Create(...)`, méthodes de transition qui renvoient `Result`. Pas de soft-delete.
2. **Infrastructure** : `IEntityTypeConfiguration<T>` (Fluent API, snake_case) + `DbSet<T>` dans `AppDbContext` + migration.
3. **Application** : DTO (`record` + `From(entity)`), spécifications, commands/queries + handlers.
4. **Web.Api** : endpoints `IEndpoint` sous `Modules/{Module}/`.

## Ajouter un log métier (source-generated)

```csharp
internal sealed partial class RefreshTokenCommandHandler(/* ..., */ ILogger<RefreshTokenCommandHandler> logger)
    : ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    // ... dans la branche de réutilisation détectée :
    LogTokenReuseDetected(logger, stored.FamilyId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Réutilisation de refresh token détectée → révocation de la famille {FamilyId}")]
    private static partial void LogTokenReuseDetected(ILogger logger, Guid familyId);
}
```

> Ne PAS logguer « début/succès/échec » : les décorateurs le font déjà. Logguer uniquement les décisions/sécurité.

## Temps réel (SignalR)

- Émettre un événement depuis un handler : injecter **`IRealtimeNotifier`** (abstraction Application) et appeler `RideStatusChangedAsync` / `NewPendingRideAsync` / `RideOfferedAsync` **après** la persistance (best-effort).
- Le hub `RideHub` (`/hubs/ride`) gère les groupes ; l'auth JWT passe en query string (`access_token`).

## Base de données

```bash
# Ajouter une migration
dotnet ef migrations add <Nom> --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations

# Les migrations s'appliquent automatiquement au démarrage (MigrateAsync) — pas de "database update" manuel en dev.
```

## Revue de code — checklist

- [ ] Commandes/requêtes renvoient `Result<T>`, handlers `internal sealed`
- [ ] Endpoint : `IEndpoint`, handler injecté, **`.ToHttpResult()`**, `RequireAuthorization` avec le bon rôle, `CancellationToken` propagé
- [ ] **Aucun** `IMediator` / `result.Match` / `ApiResults.Problem`
- [ ] Validateur FluentValidation (messages FR) si entrée à valider
- [ ] Pas d'exception pour une erreur métier (→ `Result`/`Error`)
- [ ] EF : Fluent API, snake_case, pas de soft-delete
- [ ] Logs : `[LoggerMessage]` source-generated, pas de doublon du décorateur
- [ ] Commentaires XML multi-lignes FR sur les types publics
- [ ] Tests xUnit + mocks ; respect des dépendances (NetArchTest)
