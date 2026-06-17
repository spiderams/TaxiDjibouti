# Module Drivers (Profil chauffeur) — Design

- **Version :** 1.0
- **Date :** 2026-06-16
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Legacy de référence :** `C:\prjRecherche\TaxiDjibouti` (`Driver`, `DriverEndpoints`, `DriverDtos`)

## 1. Objectif

Migrer le **profil chauffeur** du legacy vers un module **`Drivers`** dédié, en self-service : un utilisateur
au rôle `Driver` crée/met à jour son propre profil (permis, véhicule) et gère sa disponibilité. Pose l'entité
`Driver` dont le module Courses aura besoin (assignation de course) et sur laquelle se grefferont les documents
chauffeur (Identité Phase 3).

## 2. Décisions validées

- **Module `Drivers` dédié** (possède l'entité + profil + disponibilité). Le futur `Dispatch` (matching PostGIS) *lira* le `Driver`.
- **Self-service uniquement** : le chauffeur gère SON profil ; `UserId` vient du claim `sub` du JWT, jamais du body. Gestion admin → module Administration (plus tard).
- **`UserId` en `string`** (FK vers `ApplicationUser`), **un seul profil par user** (index unique).
- **Upsert** sur `POST /api/drivers` (crée si absent, sinon met à jour).
- **`AverageRating` présent mais non calculé** (calcul avec les notes du module Courses).
- Rôle **`Driver`** imposé sur les 3 endpoints.

## 3. Entité `Driver` (Domain/Drivers)

`Driver : Entity` (base SharedKernel : `int Id`, `CreatedAt`), entité riche (setters privés) :

| Propriété | Type | Défaut |
|-----------|------|--------|
| `UserId` | string | — (requis) |
| `LicenseNumber` | string | — |
| `VehiclePlate` | string | — |
| `VehicleType` | string | `"Taxi"` |
| `IsAvailable` | bool | `false` |
| `AverageRating` | double | `0` |

API domaine :
- `static Driver Create(string userId, string licenseNumber, string vehiclePlate, string vehicleType)`
- `void UpdateProfile(string licenseNumber, string vehiclePlate, string vehicleType)`
- `void SetAvailability(bool isAvailable)`

## 4. Architecture (placement par couche)

```
Domain/Drivers/Driver.cs
Application/Drivers/DriverDto.cs
Application/Drivers/DriverByUserIdSpec.cs
Application/Drivers/UpsertProfile/{UpsertDriverProfileCommand, Validator, Handler}.cs
Application/Drivers/GetMyDriver/{GetMyDriverQuery, Handler}.cs
Application/Drivers/SetAvailability/{SetAvailabilityCommand, Handler}.cs
Infrastructure/Persistence/Configurations/DriverConfiguration.cs   (+ DbSet sur AppDbContext)
Web.Api/Modules/Drivers/{UpsertDriverEndpoint, GetMyDriverEndpoint, SetAvailabilityEndpoint}.cs
```

Dépendances respectées : `Web.Api → Infrastructure → Application → Domain → SharedKernel`.

## 5. Endpoints & contrat

| Endpoint | Auth | Entrée | Sortie |
|----------|------|--------|--------|
| `POST /api/drivers` | JWT, rôle `Driver` | `{ licenseNumber, vehiclePlate, vehicleType }` | `DriverDto` |
| `GET /api/drivers/me` | JWT, rôle `Driver` | — | `DriverDto` (`404` si absent) |
| `POST /api/drivers/set-availability` | JWT, rôle `Driver` | `{ isAvailable }` | `DriverDto` |

```jsonc
// DriverDto
{ "id": 1, "userId": "<guid>", "licenseNumber": "LIC-001", "vehiclePlate": "DJ-1234",
  "vehicleType": "Taxi", "isAvailable": false, "averageRating": 0 }
```

Rôle imposé via `.RequireAuthorization(policy => policy.RequireRole(RoleNames.Driver))`. Le `userId` est extrait
du claim `sub` (fallback `NameIdentifier`) côté endpoint et passé dans la command/query — jamais dans le body.

## 6. Logique (handlers)

- **UpsertDriverProfile** : cherche le `Driver` du `UserId` (`DriverByUserIdSpec`). S'il existe → `UpdateProfile(...)` +
  `UpdateAsync`. Sinon → `Driver.Create(...)` + `AddAsync`. Renvoie `DriverDto`.
- **GetMyDriver** : `DriverByUserIdSpec` → si absent `Error.NotFound("Driver.NotFound", ...)`, sinon `DriverDto`.
- **SetAvailability** : charge le `Driver` du user → si absent `Error.NotFound(...)`, sinon `SetAvailability(isAvailable)` +
  `UpdateAsync`, renvoie `DriverDto`.

Toutes renvoient `Result<DriverDto>` ; mapping HTTP via `ResultExtensions` existant.

## 7. Validation

- `UpsertDriverProfileCommandValidator` : `LicenseNumber`, `VehiclePlate`, `VehicleType` non vides.
- `SetAvailabilityCommand` : pas de validation spécifique (bool).
- Exécutée par le décorateur de validation existant.

## 8. Accès données & infra

- `IRepository<Driver>` générique (déjà enregistré par `AddInfrastructure`) + `DriverByUserIdSpec(string userId)`.
- `DriverConfiguration` : table `drivers`, **index unique sur `user_id`**, longueurs de colonnes, `VehicleType` requis.
- `DbSet<Driver>` sur `AppDbContext`.
- Migration EF `AddDrivers` (incrémentale, snake_case).

## 9. Stratégie de test

- **Unitaires** (TDD) : `UpsertDriverProfileHandler` (cas création + cas mise à jour), `GetMyDriverHandler` (trouvé / absent),
  `SetAvailabilityHandler` — tous testables avec `IRepository<Driver>` mocké.
- **Tests d'architecture** : inchangés (Driver en Domain).
- **Vérification manuelle (Scalar)** : se connecter comme Driver → `POST /api/drivers` (création) → `GET /me` →
  `POST /api/drivers` (mise à jour) → `set-availability` → vérifier qu'un Client ne peut pas (403).

## 10. Hors périmètre

Gestion admin des chauffeurs (→ Administration), matching de proximité PostGIS (→ Dispatch), documents chauffeur
(→ Identité Phase 3), calcul de `AverageRating` (→ avec les notes du module Courses), suppression de profil.

## 11. Risques & points d'attention

- **`UserId` string** : bien typer la FK et l'index en `string` (legacy était `int`).
- **Rôle `Driver`** : nécessite que l'utilisateur ait été enregistré avec ce rôle (Identité). Un Client recevra `403`.
- **Upsert** : `POST` qui crée OU met à jour — documenté ; renvoie `200 OK` dans les deux cas (pas de `201` distinct, pour rester simple).
