# Refonte TaxiDjibouti — Migration .NET 10 + Clean Architecture (Modular Monolith)

- **Version :** 1.0
- **Date :** 2026-06-16
- **Auteur :** Samatar Y. Abdillahi
- **Statut :** Design validé — à transformer en plan d'implémentation

## 1. Contexte et objectif

TaxiDjibouti est aujourd'hui un MVP backend (`net8.0`, Minimal API, services collés à EF Core,
SignalR, .NET Aspire + PostgreSQL) avec un frontend React séparé. Le document
`Projet_Transport_Djibouti_Architecture_Technique_DRAFT.docx` (racine du repo) définit la cible :
une **plateforme de transport pour Djibouti-ville**, en **monolithe modulaire**, développée par phases.

**Objectif de ce chantier :** refondre **complètement** le backend vers une **Clean Architecture en
monolithe modulaire ciblant .NET 10**, en posant le squelette et en migrant les fonctionnalités
existantes. Objectif double : produit livrable **et** montée en compétence (pouvoir expliquer le code au lead).

Le **frontend React n'est PAS touché** (il communique par HTTP + SignalR).

## 2. Contraintes (non négociables)

- **Cible `.NET 10`** (le SDK 10.0.300 est installé et par défaut).
- **PAS de MediatR** (devenu payant). CQRS via abstractions maison + Scrutor.
- Architecture **monolithe modulaire** (pas microservices) — décision du doc d'architecture.
- **Frontend React intact.**
- Patterns inspirés de 3 projets internes de référence (voir §3).

## 3. Projets de référence

| Projet | Chemin | Ce qu'on en prend |
|--------|--------|-------------------|
| **Skoleo / IqraInstitut** | `C:\prjRecherche\IqraInstitut-backend\BackendIqraInstitut` | Layering Clean Archi, **Ardalis.Specification**, Repository, `IEntityTypeConfiguration`, FluentValidation, versions .NET 10 |
| **EchoNG / IdPreparation** | `C:\Idside\EchoNG\ms-preparation\code` | **Sans MediatR**, monolithe modulaire, pattern `IEndpoint`, Result pattern, DI modulaire |
| **Template clean-architecture** (Milan) | `C:\Users\SamatarYacinAbdillah\Downloads\clean-architecture (3)` | Mécanisme **CQRS sans MediatR** : `ICommandHandler`/`IQueryHandler` + **Scrutor** (scan + décorateurs validation/logging), `Result`/`Error` en SharedKernel, NetArchTest |

## 4. Architecture cible

### 4.1 Monolithe modulaire — variante pragmatique

Modules = **dossiers** à l'intérieur de projets en couches (≈ 5 projets), PAS un projet par module
(qui donnerait ~20 projets). Justification : gérable en solo, identique au template téléchargé, et le
doc prévoit l'extraction en modules séparés *« ultérieurement si la croissance le justifie »* (YAGNI).
Les frontières entre modules sont garanties par **tests d'architecture (NetArchTest)**.

### 4.2 Structure de la solution

```
src/
├── SharedKernel/        Result<T>/Error, Entity & AggregateRoot de base, IEndpoint,
│                        ICommandHandler/IQueryHandler, abstractions Specification
├── Domain/              Entités riches par module :
│     ├── Identity/      (User, Driver, DriverDocument)
│     ├── Rides/         (Ride, RideStatus)
│     ├── Pricing/       (ZonePrice)
│     └── Dispatch/ ...
├── Application/         CQRS par module (Commands/Queries + Handlers + Validators) :
│     ├── Identity/  Rides/  Pricing/  Dispatch/  Admin/
│     └── Abstractions/  (IRepository<T>, IBlobStorage, IRideRealtimeNotifier…)
├── Infrastructure/      EF Core (DbContext, IEntityTypeConfiguration), repositories,
│                        ASP.NET Identity, JWT, SignalR, Azure Blob, Specification evaluator
└── Web.Api/             Hôte : endpoints (IEndpoint), middleware, DI, hub SignalR

TaxiDjibouti.AppHost     .NET Aspire (orchestration PostgreSQL) — réutilisé/recréé en net10
TaxiDjibouti.Frontend    React — NON touché
```

### 4.3 Dépendances entre couches (Clean Architecture)

`Web.Api → Infrastructure → Application → Domain → SharedKernel`. Le `Domain` ne dépend
de rien d'externe (hors SharedKernel + Ardalis.Specification). À vérifier par NetArchTest.

## 5. CQRS sans MediatR

Mécanisme retenu (issu du template Milan, le plus proche de Skoleo, gratuit) :

- **Un handler par opération** : `ICommandHandler<TCommand,TResponse>` / `IQueryHandler<TQuery,TResponse>`,
  retournant `Result<T>`.
- **Scrutor** scanne et enregistre tous les handlers automatiquement.
- **Décorateurs Scrutor** pour le transverse (équivalent gratuit des pipeline behaviors MediatR) :
  - `ValidationDecorator` → exécute les validators FluentValidation avant le handler.
  - `LoggingDecorator` → log structuré entrée/sortie/erreur.
- **Endpoints** : pattern `IEndpoint` (façon EchoNG). L'endpoint injecte directement le handler concerné
  et convertit le `Result<T>` en réponse HTTP (`ProblemDetails` si échec).

```csharp
// SharedKernel
public interface ICommandHandler<in TCommand, TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}

// Application/Rides
public sealed record RequestRideCommand(int ClientId, string PickupZone, string DestinationZone, ...);
public sealed class RequestRideCommandHandler : ICommandHandler<RequestRideCommand, RideDto> { ... }

// Web.Api endpoint
app.MapPost("/api/rides/request", async (RequestRideCommand cmd,
    ICommandHandler<RequestRideCommand, RideDto> handler, CancellationToken ct)
        => (await handler.Handle(cmd, ct)).ToHttpResult());
```

**Style retenu :** handler par opération (granularité Skoleo) plutôt que service-à-méthodes (EchoNG),
pour la valeur pédagogique et l'alignement mental avec Skoleo que le lead connaît.

## 6. Patterns par préoccupation

| Préoccupation | Choix | Source |
|---|---|---|
| Result pattern | `Result<T>` + erreurs typées → `ProblemDetails` | SharedKernel (Milan) |
| Requêtes | **Ardalis.Specification** | Skoleo (+ note CLAUDE.md « même pattern spec ») |
| Repository | générique `IRepository<T>` + specifications | Skoleo |
| Validation | FluentValidation via **décorateur** Scrutor | Milan |
| Config EF | `IEntityTypeConfiguration<T>` | Skoleo |
| Endpoints | pattern `IEndpoint` (Minimal API, auto-découverts) | EchoNG |
| Authentification | **ASP.NET Core Identity** + JWT | Skoleo (décision validée) |
| Documents chauffeur | **Azure Blob Storage** (module Identité) | doc + besoin métier |
| Temps réel | SignalR (`RideHub`) derrière `IRideRealtimeNotifier` | existant |
| Tests d'architecture | **NetArchTest** (frontières modules + sens des dépendances) | Milan/EchoNG |
| Doc API | **Scalar** | Skoleo |

## 7. Stack & versions

- **`.NET 10`**, **gestion centralisée des packages** (`Directory.Packages.props` + `Directory.Build.props`).
- EF Core 10 + Npgsql 10 + **PostGIS** (préparé pour le matching de proximité — module Dispatch).
- Scrutor, FluentValidation, Serilog, Scalar, ASP.NET Core Identity, Azure.Storage.Blobs.
- **Écartés :** MediatR (payant), Finbuckle multi-tenant (mono-tenant), Redis (Phase 2+).

## 8. Découpage en modules et correspondance avec l'existant

| Module (doc) | Code actuel migré | Périmètre du chantier |
|---|---|---|
| **Identité** | `AuthService`, `User`, `Driver` (+ ASP.NET Identity, + `DriverDocument`/Blob) | ✅ Migrer + nouvelle feature documents |
| **Courses** (Rides) | `RideService`, `Ride`, endpoints Rides, `RideHub`, `Rating`, `Report` | ✅ Migrer |
| **Tarification** (Pricing) | `ZonePrice`, `GetEstimatedPriceAsync` | ✅ Migrer |
| **Dispatch** | `set-available`, `pending`, `accept` (version simple) | ✅ Migrer (PostGIS plus tard) |
| **Administration** | `AdminEndpoints` (stats, listes) | ✅ Migrer |
| **Paiement** (D-Money) | — | ⏳ Stub (dossier + interfaces) |
| **Notifications** | — | ⏳ Stub (dossier + interfaces) |

## 9. Approche d'exécution

1. **Localisation :** nouvelle solution dans un **dossier séparé** `C:\prjRecherche\Taxi` (déjà créé).
   L'ancien projet `C:\prjRecherche\TaxiDjibouti` reste comme référence de comportement.
   ⚠️ Le nouveau dossier n'a **pas encore de repo git** → `git init` à faire en tout début de plan.
2. **L'hôte Aspire .NET 10 est déjà créé** (Aspire SDK 13.1.0) : projets `Taxi.AppHost`
   (AppHost.cs mono-fichier), `Taxi.ServiceDefaults`, solution `Taxi.slnx`. Point de départ du squelette.
3. **« Porter » = réécrire**, pas copier-coller : le comportement de l'ancien code sert de spécification ;
   le nouveau code respecte les patterns ci-dessus.
4. Migration **module par module**, avec un checkpoint (build + test) après chaque module.

## 10. Ordre de migration

1. Squelette solution + `SharedKernel` + `Web.Api` qui démarre (`/health`) + AppHost Aspire + PostgreSQL.
2. Gestion centralisée des packages + `Directory.Build.props` (net10).
3. Module **Identité** (ASP.NET Identity + JWT).
4. Module **Tarification**.
5. Module **Courses** (+ Rating/Report).
6. Module **Dispatch** (dispo / pending / accept).
7. Module **Administration**.
8. Temps réel SignalR (`RideHub` + `IRideRealtimeNotifier`) + Azure Blob (documents chauffeur).
9. Stubs **Paiement** / **Notifications**.
10. **Tests d'architecture** (NetArchTest) + nettoyage.

## 11. Stratégie de test

- **Tests d'architecture** (NetArchTest) : sens des dépendances + isolation des modules.
- **Tests unitaires** sur les handlers (logique métier) avec Moq.
- **Vérification manuelle** via Scalar + le frontend React existant (non régression fonctionnelle).

## 12. Points ouverts / à challenger ultérieurement

- Granularité Application : handler-par-opération (retenu) vs service-à-méthodes (EchoNG).
- `Result` maison (SharedKernel) vs librairie `FluentResults` (EchoNG).
- Conserver l'auth maison BCrypt+JWT vs basculer sur ASP.NET Identity (retenu : Identity).
- Versions exactes des packages Aspire pour .NET 10 (l'existant est en 9.5.2).

## 13. Hors périmètre

Bot WhatsApp, intégration D-Money, app chauffeur Flutter, PWA client, Redis, multi-tenant,
montée en charge — relèvent des phases ultérieures du doc d'architecture.
