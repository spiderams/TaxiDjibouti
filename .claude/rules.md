# Règles de code — TaxiDjibouti

## Principes généraux

- **Clean Architecture** : les dépendances pointent vers l'intérieur (`Web.Api → Application → Domain → SharedKernel`). `Infrastructure` implémente les abstractions de `Application`.
- Le **Domain** n'a **aucune** dépendance externe applicative (sauf `SharedKernel` et NetTopologySuite pour la géo).
- **CQRS** : commandes pour les écritures, requêtes pour les lectures.
- **Result pattern** : pas d'exception pour les erreurs métier — on retourne `Result`/`Result<T>`.
- **Specification** : requêtes via Ardalis.Specification.

## ⛔ Interdits absolus

- **MediatR / `IMediator` / `mediator.Send(...)`** → bibliothèque payante, **bannie**. On utilise les handlers maison injectés directement.
- **`result.Match(...)` / `ApiResults.Problem(...)`** → on utilise **`.ToHttpResult()`**.
- **Serilog / Seq** → logging source-generated + OpenTelemetry (dashboard Aspire).
- **Swagger / SwaggerUI** → la doc API est **Scalar**.
- **Soft-delete** (`IsDeleted`, `DeletedAt`) et **`Guid` en clé primaire** → les entités utilisent **`int Id`**.
- **`docker-compose up` manuel** → Aspire orchestre l'infra.

## Conventions C#

- `file-scoped namespaces`, primary constructors.
- `record` pour les commands, queries, DTOs.
- `var` quand le type est évident à droite.
- Suffixe `Async` sur toute méthode asynchrone ; `CancellationToken` propagé partout.
- Pattern matching plutôt que tests de type.
- Commentaires XML **multi-lignes** en **français** sur les types et membres publics (jamais en une seule ligne).

## Nommage

- Commandes : `{Action}{Entité}Command` (ex. `RequestRideCommand`)
- Requêtes : `Get{...}Query` / `{...}Query` (ex. `FindNearestDriversQuery`)
- Handlers : `{Command|Query}Handler` (ex. `AcceptRideCommandHandler`)
- Validateurs : `{Command}Validator`
- Spécifications : `{Entité}{But}Spec` (ex. `RideByIdSpec`, `ExpiredOffersSpec`)
- Endpoints : `{Action}{Entité}Endpoint` ou `{Module}Endpoints` (implémentent `IEndpoint`)

## CQRS sans MediatR

Interfaces dans `Taxi.SharedKernel.Messaging`. Un handler `internal sealed` :

```csharp
internal sealed class AcceptRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : ICommandHandler<AcceptRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(AcceptRideCommand command, CancellationToken cancellationToken)
    {
        // ... charge, applique la règle (méthode de l'agrégat qui renvoie Result), persiste
        return RideDto.From(ride); // conversion implicite vers Result<RideDto>
    }
}
```

Enregistrement automatique par **Scrutor** (`AddApplication`, scan `publicOnly:false`) + décorateurs via `TryDecorate` (Validation puis Logging). **Aucune** inscription manuelle des handlers.

## Endpoints (Minimal API + IEndpoint)

```csharp
public sealed class AcceptRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/accept", async (
            int id,
            ClaimsPrincipal principal,
            ICommandHandler<AcceptRideCommand, RideDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new AcceptRideCommand(id, userId), ct);
            return result.ToHttpResult();              // <-- toujours ToHttpResult()
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
        .WithName("AcceptRide")
        .WithTags(Tags.Rides)
        .WithSummary("Accepter une course");
    }
}
```

- Un endpoint = un fichier sous `Web.Api/Modules/{Module}/`.
- Handler **injecté directement** dans la lambda.
- `principal.GetUserId()` pour l'id utilisateur (claim `sub`).
- Autorisation par rôle : `RequireAuthorization(p => p.RequireRole(RoleNames.X))`.
- `Tags` statiques pour regrouper dans Scalar.

## Result pattern

```csharp
return Result.Failure<RideDto>(RideErrors.NotFound);  // échec
return RideDto.From(ride);                             // succès (conversion implicite)
```

Mapping HTTP : `ResultExtensions.ToHttpResult()` (Validation→400, Unauthorized→401, Forbidden→403, NotFound→404, Conflict→409, Failure→500).

## FluentValidation

- Un validateur par commande, messages en **français**.
- Appliqué automatiquement par le `ValidationDecorator` (commande **et** requête) — pas d'appel manuel.

## EF Core

- Fluent API uniquement (`IEntityTypeConfiguration<T>`), pas de Data Annotations.
- `AppDbContext : IdentityDbContext`, **snake_case** (`EFCore.NamingConventions`).
- PostGIS : `modelBuilder.HasPostgresExtension("postgis")` ; position en `geography(Point, 4326)` + index GiST ; `NetTopologySuite.Geometries.Point`.
- Repository générique : `IRepository<T> : IRepositoryBase<T>` (Ardalis).
- Migration (factory design-time `AppDbContextFactory`) :

```bash
dotnet ef migrations add <Nom> --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```

## Logging

- **`[LoggerMessage]`** source-generated uniquement (jamais de string interpolée dans un log).
- Le cycle de vie commande/requête est **déjà tracé** par les décorateurs → **ne pas re-logguer** « début/succès/échec » dans un handler.
- Logguer seulement les **décisions métier** et **événements de sécurité** (ex. réutilisation de token, offre faite à un chauffeur).
- Voir `docs/conventions-logging-et-commentaires.md`.

## Sécurité

- Pas de secret en dur.
- `RequireAuthorization(p => p.RequireRole(RoleNames.X))` sur les endpoints protégés.
- Rôles : `Client`, `Driver`, `Admin`.
- Toute entrée validée via FluentValidation.
- Hub SignalR `[Authorize]`, JWT en query string (`access_token`).

## Tests

- xUnit + Moq + FluentAssertions ; TDD pour la logique métier.
- Tests d'architecture (NetArchTest) : dépendances inward-only.
- Handlers `internal` testés via `InternalsVisibleTo("Taxi.Application.Tests")`.
- Mock des dépendances : `Mock<IRepository<T>>`, `It.IsAny<ISpecification<T>>()`, `NullLogger<T>.Instance` pour les loggers.
