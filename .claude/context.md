# Contexte projet — TaxiDjibouti

## C'est quoi ?

**TaxiDjibouti** est une application de réservation de **taxi / VTC à Djibouti**. Ce dépôt contient le **backend**, refondu en **Clean Architecture .NET 10** (depuis un ancien backend .NET 8 Minimal API). Le frontend React est séparé (dernière phase, hors de ce dépôt).

Solution : `Taxi.slnx`, orchestrée par **.NET Aspire**.

## Phase actuelle

Backend MVP — modules livrés :

- **Identité** — inscription / connexion par téléphone, JWT maison + refresh tokens (rotation + détection de réutilisation)
- **Tarification** — prix par zones
- **Drivers** — profil chauffeur, disponibilité
- **Courses (Rides)** — cycle de vie (demande → offre → accept → arrivé → en course → terminée / annulée), notation, signalement
- **Administration** — statistiques + listes (lecture seule)
- **Dispatch** — matching proximité (PostGIS) + auto-assignation séquentielle (offre 30 s, réattribution)
- **Temps réel** — SignalR `RideHub` (position chauffeur, statuts, offres)
- **Transverse** — gestion d'exceptions, headers OWASP, logging source-generated

Pas encore fait : Identité Phase 3 (documents chauffeur + Azure Blob), Paiement (D-Money), Notifications (FCM/SMS), mise à jour du frontend React.

## Structure du dépôt

```
Taxi/
├── Taxi.slnx
├── Directory.Packages.props          # Central Package Management (versions)
├── Directory.Build.props
├── Taxi.AppHost/                     # Orchestrateur Aspire (AppHost.cs) — projet de démarrage
├── Taxi.ServiceDefaults/             # Extensions Aspire (OpenTelemetry, health checks)
│
├── src/
│   ├── Taxi.SharedKernel/
│   │   ├── Entity.cs                 # int Id + CreatedAt (pas de soft-delete, pas de Guid)
│   │   ├── Result.cs                 # Result / Result<T>
│   │   ├── Error.cs                  # Error(Code, Description, ErrorType)
│   │   └── Messaging/                # ICommand, IQuery, ICommandHandler, IQueryHandler
│   │
│   ├── Taxi.Domain/
│   │   ├── Identity/                 # ApplicationUser, RefreshToken, RoleNames
│   │   ├── Drivers/                  # Driver (position geography), DriverErrors
│   │   ├── Rides/                    # Ride (agrégat riche), RideStatus, Rating, Report, RideErrors
│   │   └── Pricing/                  # ZonePrice
│   │
│   ├── Taxi.Application/
│   │   ├── Abstractions/             # IRepository<T>, Behaviors (ValidationDecorator, LoggingDecorator, RequestLog)
│   │   ├── Identity/                 # Auth : Register, Login, Refresh, Revoke, GetMe (+ AuthTokenIssuer, ITokenService)
│   │   ├── Pricing/                  # EstimatePrice
│   │   ├── Drivers/                  # UpsertProfile, GetMyDriver, SetAvailability (+ specs)
│   │   ├── Rides/                    # Request, Accept, Transitions, Cancel, Rate, Reporting, MyRides, Pending (+ RideSpecs)
│   │   ├── Administration/           # Stats + Listing (IUserDirectory)
│   │   ├── Dispatch/                 # IDriverLocator, IRideDispatcher, FindNearestDrivers, AcceptOffer, DeclineOffer
│   │   ├── Realtime/                 # IRealtimeNotifier, UpdateDriverLocation, RideAccess
│   │   └── DependencyInjection.cs    # Scrutor scan + TryDecorate (Validation puis Logging)
│   │
│   ├── Taxi.Infrastructure/
│   │   ├── Persistence/              # AppDbContext (IdentityDbContext), Configurations, Migrations, AppDbContextFactory, Repository<T>
│   │   ├── Identity/                 # TokenService, JwtSettings, IdentitySeeder, RefreshTokenCleanupService, UserDirectory
│   │   ├── Dispatch/                 # DriverLocator (PostGIS), OfferTimeoutService (BackgroundService)
│   │   └── DependencyInjection.cs
│   │
│   └── Taxi.Web.Api/
│       ├── Program.cs                # AddServiceDefaults, pipeline, MapEndpoints, MapHub
│       ├── Endpoints/                # IEndpoint, EndpointExtensions, ResultExtensions (ToHttpResult), Tags, ClaimsPrincipalExtensions
│       ├── Middleware/               # GlobalExceptionHandler, SecurityHeadersMiddleware
│       ├── Modules/                  # endpoints par module (Identity, Drivers, Rides, Admin, Dispatch, Pricing)
│       ├── Realtime/                 # RideHub, SignalRRealtimeNotifier, DriverLocationDto
│       └── OpenApi/                  # BearerSecuritySchemeTransformer (Scalar)
│
├── tests/
│   ├── Taxi.Application.Tests/       # xUnit + Moq + FluentAssertions
│   └── Taxi.Architecture.Tests/      # NetArchTest (dépendances inward-only)
│
└── docs/
    ├── conventions-logging-et-commentaires.md
    └── superpowers/{specs,plans}/    # specs & plans d'implémentation par module
```

## Règles métier clés

1. **Auth** : login par **numéro de téléphone**. Rôles `Client`, `Driver`, `Admin`. Refresh tokens avec rotation ; rejouer un token révoqué → toute la `FamilyId` est révoquée (sécurité).
2. **Cycle de course** (`RideStatus`) : `Pending → Offered → Accepted → DriverArrived → InProgress → Completed` (ou `Cancelled`). Transitions portées par l'agrégat `Ride` (retournent `Result`).
3. **Dispatch** : à la demande, on offre la course au chauffeur disponible le plus proche (PostGIS). S'il refuse / ne répond pas en 30 s → offre au suivant. Plus personne → retour `Pending` (flux manuel). Accepter une course rend le chauffeur indisponible ; terminer/annuler le rend disponible.
4. **Position chauffeur** : stockée en `geography(Point, 4326)` (PostGIS / NetTopologySuite), diffusée en temps réel via SignalR.
5. **Pas de pagination** pour l'instant (MVP).

## Intégrations externes

| Service | Usage | Statut |
|---------|-------|--------|
| PostgreSQL + PostGIS | base + requêtes géospatiales | **actif** (conteneur Aspire) |
| Azure Blob Storage | documents chauffeur | **prévu** (Identité Phase 3) |
| D-Money | paiement | **prévu** (stub) |
| FCM / SMS | notifications | **prévu** (stub) |

## Démarrage local

```bash
# Prérequis : .NET 10 SDK + Docker Desktop
# Lancer via Aspire (démarre Postgres+PostGIS automatiquement) :
dotnet run --project Taxi.AppHost
# ou F5 sur Taxi.AppHost dans Visual Studio / Rider

# Dashboard Aspire : http://localhost:15888
# Doc API (Scalar, dev) : http://localhost:5000/scalar
```
