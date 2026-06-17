# Module Administration — Design

- **Version :** 1.0
- **Date :** 2026-06-17
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Legacy de référence :** `C:\prjRecherche\TaxiDjibouti` (`AdminEndpoints`)

## 1. Objectif

Back-office en **lecture seule** pour l'administrateur : statistiques (compteurs) et listes des utilisateurs,
chauffeurs, courses et signalements. Tout en rôle `Admin`.

## 2. Décisions validées

- **`IUserDirectory`** : abstraction en couche Application pour lire les utilisateurs (`ApplicationUser` n'est pas une entité du domaine, donc pas de `IRepository`), implémentée en Infrastructure avec `UserManager`/EF → la couche Application reste **sans dépendance EF**.
- **Réutilisation** des DTOs existants `DriverDto`, `RideDto`, `ReportDto` ; nouveaux `AdminStatsDto`, `UserSummary`.
- **5 endpoints `GET /api/admin/*`** en rôle `Admin`, lecture seule.
- **Pas de pagination** (on renvoie tout — MVP, volume faible) ; pas d'actions de modération.

## 3. Lecture des utilisateurs

**`IUserDirectory`** (Application, `Taxi.Application.Administration`) :
```csharp
public interface IUserDirectory
{
    Task<int> CountAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken);
}
public sealed record UserSummary(string Id, string FullName, string PhoneNumber, IReadOnlyList<string> Roles);
```

**`UserDirectory`** (Infrastructure, `Taxi.Infrastructure.Identity`) : injecte `UserManager<ApplicationUser>`.
- `CountAsync` → `userManager.Users.CountAsync(ct)` (EF, disponible en Infrastructure).
- `ListAsync` → charge les users (`userManager.Users.ToListAsync(ct)`) puis, pour chacun, `GetRolesAsync` (N+1 acceptable en MVP admin) → mappe en `UserSummary`.
- Enregistré en DI : `services.AddScoped<IUserDirectory, UserDirectory>()`.

## 4. Queries (CQRS, lecture seule)

| Query | Handler — source | Renvoie |
|-------|------------------|---------|
| `GetAdminStatsQuery` | `IUserDirectory.CountAsync` + `IRepository<Driver>.CountAsync()` + `IRepository<Ride>.CountAsync()` + `IRepository<Report>.CountAsync()` | `AdminStatsDto` |
| `GetUsersQuery` | `IUserDirectory.ListAsync` | `IReadOnlyList<UserSummary>` |
| `GetDriversQuery` | `IRepository<Driver>.ListAsync()` → `DriverDto.From` | `IReadOnlyList<DriverDto>` |
| `GetAllRidesQuery` | `IRepository<Ride>.ListAsync()` → `RideDto.From` | `IReadOnlyList<RideDto>` |
| `GetReportsQuery` | `IRepository<Report>.ListAsync()` → `ReportDto.From` | `IReadOnlyList<ReportDto>` |

`AdminStatsDto` :
```csharp
public sealed record AdminStatsDto(int Users, int Drivers, int Rides, int Reports);
```

Les compteurs/listes des entités du domaine utilisent les méthodes génériques d'Ardalis `CountAsync(CancellationToken)`
et `ListAsync(CancellationToken)` (sans Specification = « tous »). Pas de `Report`-namespace clash : les queries Admin
vivent dans `Taxi.Application.Administration` (pas `.Report`).

## 5. Endpoints (rôle `Admin`)

| Endpoint | Renvoie |
|----------|---------|
| `GET /api/admin/stats` | `AdminStatsDto` |
| `GET /api/admin/users` | `UserSummary[]` |
| `GET /api/admin/drivers` | `DriverDto[]` |
| `GET /api/admin/rides` | `RideDto[]` |
| `GET /api/admin/reports` | `ReportDto[]` |

Tous : `.RequireAuthorization(p => p.RequireRole(RoleNames.Admin))`, tag `Admin` (ajout `Tags.Admin`), convention OpenAPI
(`WithName`/`WithSummary`/`WithDescription`). Pas de `userId` nécessaire (vues globales admin).

## 6. Architecture (placement)

```
Taxi.Application/Administration/IUserDirectory.cs, UserSummary.cs, AdminStatsDto.cs
Taxi.Application/Administration/Stats/{GetAdminStatsQuery,Handler}.cs
Taxi.Application/Administration/Users/{GetUsersQuery,Handler}.cs
Taxi.Application/Administration/Drivers/{GetDriversQuery,Handler}.cs
Taxi.Application/Administration/Rides/{GetAllRidesQuery,Handler}.cs
Taxi.Application/Administration/Reports/{GetReportsQuery,Handler}.cs
Taxi.Infrastructure/Identity/UserDirectory.cs  (+ DI registration)
Taxi.Web.Api/Modules/Admin/*Endpoint.cs  (5 endpoints) + Tags.Admin
```

Le handler `GetDrivers`/`GetAllRides`/`GetReports` réutilise les `DriverDto`/`RideDto`/`ReportDto` (dans `Taxi.Application.Rides`/`.Drivers`) via les `using` adéquats.

## 7. Stratégie de test

- **`GetAdminStatsQueryHandler` (TDD)** : `IUserDirectory` + 3 `IRepository<T>` mockés (CountAsync) → `AdminStatsDto` agrège correctement.
- **Handlers de listes** : 1-2 tests légers (repo `ListAsync` mocké → DTOs mappés).
- **`UserDirectory`** (Infra, `UserManager`) : difficile à mocker proprement → **vérification manuelle (Scalar)**.
- **Vérification manuelle** : se connecter en Admin → `stats` (compteurs cohérents avec les données créées) → `users`/`drivers`/`rides`/`reports` (listes non vides) ; un Client/Driver → **403**.

## 8. Hors périmètre

Actions de modération (résoudre/clore un signalement), gestion des utilisateurs (bloquer/supprimer/changer rôle),
pagination, tri/filtres, détails d'une entité par id (vues détaillées).

## 9. Risques & points d'attention

- **Compte/liste users via `UserManager`** : `Users.CountAsync`/`ToListAsync` nécessitent EF (OK en Infrastructure). Le `GetRolesAsync` par user est un N+1 → acceptable pour un admin à faible volume (optimisation possible plus tard).
- **Pas de compte `Admin` par défaut** : pour tester, il faut un utilisateur enregistré avec le rôle `Admin` (via `register` role=Admin). À rappeler pour la vérif manuelle.
- **Réutilisation des DTOs** : ne pas dupliquer `DriverDto`/`RideDto`/`ReportDto` ; juste les référencer.
