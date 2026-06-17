# TaxiDjibouti — Instructions projet pour Claude / IA

Ce fichier décrit les conventions, patterns et règles absolues de ce projet.  
**Lire entièrement avant de générer du code.**

---

## Contexte

**TaxiDjibouti** est une application de réservation de taxi/VTC à Djibouti.  
Solution : `Taxi.slnx`. Backend .NET 10, orchestré par .NET Aspire.  
Sources sous `src/`, tests sous `tests/`.

---

## Architecture en couches (Clean Architecture)

Les dépendances pointent **toujours vers l'intérieur** :

```
Taxi.Web.Api
    └─► Taxi.Application
            └─► Taxi.Domain
                    └─► Taxi.SharedKernel
Taxi.Infrastructure ──► Taxi.Application (implémente les abstractions)
```

- **SharedKernel** : `Entity`, `Result`/`Error`, interfaces CQRS, `IEndpoint`
- **Domain** : agrégats riches, value objects, erreurs domaine — zéro dépendance externe
- **Application** : handlers, validateurs, abstractions (`IRealtimeNotifier`, `IDriverLocator`, `IRideDispatcher`)
- **Infrastructure** : EF Core, repositories, `TokenService`, `OfferTimeoutService`
- **Web.Api** : endpoints Minimal API, middlewares, `RideHub` SignalR

### Modules métier (dossiers dans Application / Web.Api)

`Identity` | `Pricing` | `Drivers` | `Rides` | `Administration` | `Dispatch` | `Realtime`

---

## CQRS SANS MediatR (POINT CRITIQUE)

**MediatR est interdit** (bibliothèque payante). Le projet utilise des interfaces maison dans `Taxi.SharedKernel.Messaging` :

```csharp
public interface ICommand<TResponse> { }
public interface IQuery<TResponse> { }
public interface ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct = default);
}
public interface IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct = default);
}
```

Enregistrement via **Scrutor** (scan automatique, `publicOnly: false`).  
Décorateurs empilés via `TryDecorate` : **Validation** (FluentValidation) puis **Logging** (le plus externe).

### Écrire un endpoint (pattern obligatoire)

```csharp
// Le handler est injecté DIRECTEMENT dans la lambda — jamais via IMediator.
app.MapPost("/rides", async (
    CreateRideCommand command,
    ICommandHandler<CreateRideCommand, RideDto> handler,
    ClaimsPrincipal principal,
    CancellationToken ct) =>
{
    return (await handler.Handle(command, ct)).ToHttpResult(); // <-- ToHttpResult(), pas result.Match
})
.RequireAuthorization(p => p.RequireRole(RoleNames.Client))
.WithTags(Tags.Rides);
```

Chaque endpoint implémente `IEndpoint` et est **auto-découvert** au démarrage :

```csharp
public sealed class CreateRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) { ... }
}
```

---

## Result pattern

- `Result` / `Result<T>` + `Error(Code, Description, ErrorType)` — tout dans `Taxi.SharedKernel`
- **Jamais d'exceptions pour les erreurs métier**
- Mapping HTTP via `ResultExtensions.ToHttpResult()` :

| ErrorType | HTTP |
|-----------|------|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| Failure | 500 |

---

## Entité de base

```csharp
public abstract class Entity
{
    public int Id { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
```

- Id = **`int`** (pas de Guid)
- **Pas de soft-delete** (`IsDeleted`, `DeletedAt`, etc. n'existent pas)
- **Pas d'audit** `UpdatedBy` / `UpdatedAt`
- Les agrégats exposent des méthodes de transition riches qui retournent `Result`

---

## Identité et JWT

- `ApplicationUser : IdentityUser` (clé string/GUID)
- **Login par numéro de téléphone** (`UserName = PhoneNumber`)
- Rôles : **`Client`** / **`Driver`** / **`Admin`** (constants dans `RoleNames`)
- JWT maison via `ITokenService` / `TokenService` (`JsonWebTokenHandler`)
- Refresh tokens : rotation + détection de réutilisation, `FamilyId`, hash SHA256
- **Pas de `SignInManager`**
- Récupération de l'id utilisateur dans un endpoint : `principal.GetUserId()` (claim `sub`)

---

## PostGIS et géolocalisation

- Extension PostgreSQL activée : `HasPostgresExtension("postgis")`
- Colonne position chauffeur : `geography(Point, 4326)` via **NetTopologySuite**
- Recherche de proximité via **`IDriverLocator`** (PostGIS dans l'infrastructure)
- Toujours utiliser `NetTopologySuite.Geometries.Point` pour les coordonnées géographiques

---

## SignalR — RideHub

- Hub : `RideHub` sur `/hubs/ride`
- Authentification JWT en **query string** : `?access_token=<token>`
- Groupes : `Drivers`, `Admins`, `Client_{id}`, `Ride_{id}`, `DriverUser_{id}`
- Événements émis : `driverLocationUpdated`, `rideStatusChanged`, `newPendingRide`, `rideOffered`
- Abstraction dans Application : `IRealtimeNotifier` (implémentée dans Web.Api)

---

## Logging

- Utiliser **`[LoggerMessage]`** source-generated (pas de string interpolation directe dans les logs)
- Catalogue des messages dans `RequestLog`
- Les décorateurs CQRS tracent automatiquement toutes les commandes/requêtes — **ne pas re-logguer le cycle de vie** dans les handlers
- Logguer uniquement les **décisions métier** et les **événements de sécurité**
- Sink : **OpenTelemetry → dashboard Aspire** (pas de Serilog, pas de Seq)

---

## Persistance et migrations

- `AppDbContext : IdentityDbContext`, snake_case via `EFCore.NamingConventions`
- Configurations dans des classes `IEntityTypeConfiguration<T>` séparées
- Repository générique Ardalis : `IRepository<T> : IRepositoryBase<T>`
- Migrations appliquées au démarrage (`MigrateAsync`) + seed des rôles

**Commande pour ajouter une migration :**

```bash
dotnet ef migrations add <NomMigration> \
  --project src/Taxi.Infrastructure \
  --startup-project src/Taxi.Web.Api \
  --output-dir Persistence/Migrations
```

---

## Démarrage du projet

Aspire orchestre tout — **pas besoin de `docker-compose up` manuel**.

```bash
# Ligne de commande
dotnet run --project Taxi.AppHost

# Ou F5 sur Taxi.AppHost dans Visual Studio / Rider
# (attache le débogueur à tous les projets enfants)
```

Aspire démarre le conteneur PostgreSQL+PostGIS automatiquement (image `postgis/postgis`).

| Ressource | URL |
|-----------|-----|
| Dashboard Aspire | http://localhost:15888 |
| Documentation API (Scalar) | http://localhost:5000/scalar *(dev seulement)* |

---

## Conventions C#

- `file-scoped namespaces`
- Primary constructors
- `record` pour les commands, queries et DTOs
- `var` quand le type est évident
- Suffixe `Async` sur toutes les méthodes asynchrones
- `CancellationToken ct` partout
- Commentaires XML **multi-lignes** en **français** sur tous les types et membres publics
- Validation FluentValidation avec messages en **français**, appliquée via décorateur
- Pas de pagination pour l'instant (MVP)

---

## Interdits / pièges à éviter absolument

| Interdit | Raison / Alternative |
|----------|---------------------|
| `MediatR` / `IMediator` / `mediator.Send(...)` | Payant — utiliser les handlers maison injectés directement |
| `result.Match(...)` | Ne pas utiliser — appeler `.ToHttpResult()` |
| `ApiResults.Problem(...)` | Ne pas utiliser — `.ToHttpResult()` gère tout |
| `Serilog` | Non utilisé — logging source-generated + OpenTelemetry |
| `Seq` | Non utilisé — dashboard Aspire pour les logs/traces |
| `Swagger` / `SwaggerUI` | Non utilisé — **Scalar** est la doc API |
| Soft-delete (`IsDeleted`, `DeletedAt`) | N'existe pas dans ce projet |
| `Guid` comme clé primaire | Les entités utilisent `int Id` |
| Exceptions pour les erreurs métier | Toujours retourner `Result`/`Error` |
| `docker-compose up` manuel | Aspire gère l'infrastructure — lancer via `Taxi.AppHost` |
| Re-logger le cycle de vie dans les handlers | Les décorateurs s'en chargent déjà |
| `string interpolation` dans les logs | Utiliser `[LoggerMessage]` source-generated |
