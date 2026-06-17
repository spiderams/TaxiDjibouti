# TaxiDjibouti — Application de réservation de taxi/VTC

Backend de la plateforme **TaxiDjibouti**, un service de réservation de taxi et VTC à Djibouti.  
Refonte en **Clean Architecture .NET 10** (depuis un ancien backend .NET 8 Minimal API).  
Un frontend React existe séparément (hors de ce dépôt).

---

## Démarrage

### Prérequis

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (pour la base PostgreSQL+PostGIS gérée par Aspire)

### Lancer le projet

```bash
# Option 1 — depuis Visual Studio / Rider
# Définir Taxi.AppHost comme projet de démarrage, puis F5
# Le débogueur s'attache automatiquement à tous les projets enfants.

# Option 2 — ligne de commande
dotnet run --project Taxi.AppHost
```

Aspire démarre automatiquement un conteneur PostgreSQL+PostGIS (image `postgis/postgis`, volume persistant).

| Ressource | URL (défaut) |
|-----------|-------------|
| Dashboard Aspire (logs / traces) | <http://localhost:15888> |
| Documentation API (Scalar) | <http://localhost:5000/scalar> *(en mode dev uniquement)* |

---

## Stack technique

| Couche | Technologies |
|--------|-------------|
| Runtime | .NET 10 |
| Orchestration | .NET Aspire |
| Base de données | PostgreSQL + PostGIS (NetTopologySuite) |
| ORM | EF Core 10 + Npgsql + EFCore.NamingConventions (snake_case) |
| CQRS | Interfaces maison `ICommand<T>` / `IQuery<T>` + handlers — **pas de MediatR** |
| Result pattern | `Result` / `Result<T>` + `Error` maison — pas d'exceptions métier |
| Validation | FluentValidation (décorateur, messages en français) |
| Spécifications | Ardalis.Specification |
| Identité | ASP.NET Core Identity + JWT maison + refresh tokens (rotation) |
| Temps réel | SignalR in-process (`RideHub`) |
| Documentation API | Scalar (`/scalar`) |
| Logging / Traces | Microsoft.Extensions.Logging source-generated + OpenTelemetry (sink : dashboard Aspire) |
| Tests | xUnit + Moq + FluentAssertions + NetArchTest |
| Package management | Central Package Management (`Directory.Packages.props`) |

---

## Architecture

La solution `Taxi.slnx` suit une **Clean Architecture en monolithe modulaire**.  
Les modules métier sont organisés en dossiers à l'intérieur de chaque couche.

### Projets

| Projet | Rôle |
|--------|------|
| `Taxi.SharedKernel` | Primitives partagées : `Entity`, `Result`/`Error`, interfaces CQRS (`ICommand`, `IQuery`, `ICommandHandler`, `IQueryHandler`), `IEndpoint` |
| `Taxi.Domain` | Agrégats, entités riches, value objects, erreurs domaine |
| `Taxi.Application` | Handlers CQRS, validateurs FluentValidation, abstractions (`IRealtimeNotifier`, `IDriverLocator`, `IRideDispatcher`) |
| `Taxi.Infrastructure` | EF Core (`AppDbContext`), repositories Ardalis, migrations PostGIS, `TokenService`, `OfferTimeoutService` |
| `Taxi.Web.Api` | Endpoints `IEndpoint` (Minimal API), middlewares, `RideHub` SignalR, implémentation `IRealtimeNotifier` |
| `Taxi.AppHost` | Orchestrateur Aspire : définit les ressources (Postgres, API) et leurs dépendances |
| `Taxi.ServiceDefaults` | Extensions Aspire partagées (OpenTelemetry, health checks, etc.) |

### Modules livrés

Les dossiers de fonctionnalités traversent les couches `Application` / `Web.Api` :

- **Identity** — inscription, connexion par téléphone, JWT + refresh tokens (phases 1 et 2)
- **Pricing** — tarification par zones
- **Drivers** — profil chauffeur, disponibilité
- **Rides** — cycle de vie des courses (phases 1 et 2), notation, signalement
- **Administration** — statistiques et listes (lecture seule)
- **Dispatch** — matching PostGIS (proximité) + auto-assignation séquentielle (offre 30 s)
- **Realtime** — SignalR `RideHub`, groupes et événements temps réel
- **Transverse** — `GlobalExceptionHandler`, `SecurityHeadersMiddleware` (OWASP)

---

## Commandes de développement

```bash
# Build
dotnet build Taxi.slnx

# Tests
dotnet test Taxi.slnx

# Ajouter une migration EF Core
dotnet ef migrations add <NomMigration> \
  --project src/Taxi.Infrastructure \
  --startup-project src/Taxi.Web.Api \
  --output-dir Persistence/Migrations
```

---

## Roadmap (non encore implémenté)

- **Identité Phase 3** — upload de documents chauffeur (Azure Blob Storage)
- **Paiement** — stubs D-Money
- **Notifications** — FCM (push mobile) + SMS
- **Frontend React** — mise à jour de l'interface utilisateur
