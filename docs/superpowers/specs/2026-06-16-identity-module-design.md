# Module Identité — Design (inspiré de Skoleo, sans multi-tenant)

- **Version :** 1.0
- **Date :** 2026-06-16
- **Statut :** Design validé — Phase 1 à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Spec parent :** `docs/superpowers/specs/2026-06-16-migration-net10-clean-archi-design.md`
- **Référence de pattern :** Skoleo / IqraInstitut (`C:\prjRecherche\IqraInstitut-backend\BackendIqraInstitut`)

## 1. Objectif

Construire le module **Identité** de TaxiDjibouti en adoptant le **pattern d'authentification professionnel de Skoleo**,
adapté au contexte (mono-tenant, login par téléphone). Remplace l'auth maison legacy (BCrypt + JWT artisanal).

## 2. Décisions validées

- **ASP.NET Core Identity** : `ApplicationUser : IdentityUser` (clé **`string`/GUID**, défaut Identity), `IdentityRole`,
  `AddIdentityCore` + `UserManager` + `SignInManager`, politiques de mot de passe + **lockout** (5 essais → 15 min).
- **JWT maison** via `ITokenService` (abstraction Application) / `TokenService` (Infrastructure) — **PAS** `MapIdentityApi`.
- **Refresh tokens** avec rotation + détection de réutilisation — **Phase 2** (pas Phase 1).
- **Login par téléphone** (`PhoneNumber` sert de `UserName`), pas par email.
- **Pas de multi-tenant** (on écarte Finbuckle, `UserInstitution`, `X-Institution-Id`).
- **Frontend React mis à jour** : `userId:number` → `user.id:string`, lecture de `accessToken`. (Chantier frontend séparé, après validation backend.)

## 3. Découpage en phases

| Phase | Contenu | Plan |
|-------|---------|------|
| **1 — Auth de base** | Identity + rôles + migrations EF + JWT (access token) + `register`/`login`/`me` | **CE plan** |
| **2 — Refresh & durcissement** | Refresh tokens (rotation + détection réutilisation), `revoke`/logout, service de nettoyage | Plan ultérieur |
| **3 — Documents chauffeur** | Entité `DriverDocument` + upload Azure Blob (permis, carte grise…) | Plan ultérieur |

Le reste de ce document détaille la **Phase 1**.

## 4. Architecture (Phase 1) — placement par couche

```
Domain/Identity/
  ApplicationUser.cs        (: IdentityUser ; + FullName, CreatedAt)
  RoleNames.cs              (constantes Client / Driver / Admin)
Application/Identity/
  Abstractions/ITokenService.cs        (génère le JWT)
  Abstractions/AccessToken.cs          (record { Token, ExpiresAt })
  Auth/Register/RegisterCommand.cs + Handler + Validator
  Auth/Login/LoginCommand.cs + Handler + Validator
  Auth/GetMe/GetMeQuery.cs + Handler
  Auth/AuthResponse.cs, UserInfo.cs    (DTOs de contrat)
Infrastructure/Identity/
  TokenService.cs                      (impl ITokenService, JsonWebTokenHandler)
  JwtSettings.cs                       (options : Secret, Issuer, Audience, AccessTokenLifetimeMinutes)
  IdentitySeeder.cs                    (seed des 3 rôles au démarrage)
  (AppDbContext devient IdentityDbContext — voir §6)
Web.Api/Modules/Identity/
  RegisterEndpoint.cs, LoginEndpoint.cs, MeEndpoint.cs   (IEndpoint)
```

Dépendances respectées : `Web.Api → Infrastructure → Application → Domain → SharedKernel`.
`ApplicationUser` vit dans Domain mais hérite d'`IdentityUser` (hiérarchie Identity, séparée de notre base `Entity`) — c'est le pattern Skoleo.

## 5. Modèle de données

**`ApplicationUser : IdentityUser`** (clé `string`) — hérite de `Id`, `UserName`, `PhoneNumber`, `PasswordHash`,
`SecurityStamp`, `AccessFailedCount`, `LockoutEnd`, etc. Champs ajoutés :
- `FullName` (string, requis)
- `CreatedAt` (DateTime, UTC)

À l'inscription : `UserName = PhoneNumber` (le téléphone est l'identifiant de connexion).

**Rôles** : `IdentityRole` seedés `Client`, `Driver`, `Admin` au démarrage (`IdentitySeeder`).

**Pas d'entité `Driver` en Phase 1** : le profil chauffeur (permis, véhicule, dispo) relève du module Dispatch /
de la Phase 3 (documents). La Phase 1 ne gère que l'identité et le rôle.

## 6. Impact sur la fondation existante

- **`AppDbContext` : `DbContext` → `IdentityDbContext<ApplicationUser, IdentityRole, string>`.**
  On conserve `DbSet<ZonePrice>` et `ApplyConfigurationsFromAssembly`, en appelant `base.OnModelCreating(modelBuilder)`
  **en premier** (obligatoire pour qu'Identity configure ses tables).
- **Introduction des migrations EF Core.** Première migration `InitialIdentity` (tables Identity + `zone_prices`).
  Packages : `Microsoft.EntityFrameworkCore.Design` (déjà en CPM). Migrations générées dans `Infrastructure/Persistence/Migrations`.
- **`Program.cs`** : remplacer `await db.Database.EnsureCreatedAsync()` par `await db.Database.MigrateAsync()`.
  → résout le risque « EnsureCreated bloque les migrations » noté en fondation.
- **DI** : ajouter `AddIdentityCore<ApplicationUser>(...).AddRoles<IdentityRole>().AddSignInManager().AddEntityFrameworkStores<AppDbContext>()`,
  l'enregistrement de `ITokenService`, la config `JwtSettings`, et `AddAuthentication().AddJwtBearer(...)` avec
  `TokenValidationParameters` (issuer/audience/clé, `ClockSkew = TimeSpan.Zero`) + support du token SignalR par query string
  (`/hubs`) pour préparer le temps réel.

## 7. Endpoints & contrat (Phase 1)

| Endpoint | Auth | Entrée | Sortie |
|----------|------|--------|--------|
| `POST /api/auth/register` | anonyme | `{ fullName, phoneNumber, password, role }` | `AuthResponse` |
| `POST /api/auth/login` | anonyme | `{ phoneNumber, password }` | `AuthResponse` |
| `GET /api/auth/me` | JWT | — | `UserInfo` |

```jsonc
// AuthResponse
{
  "accessToken": "<jwt>",
  "expiresAt": "2026-06-16T10:45:00Z",
  "tokenType": "Bearer",
  "user": { "id": "<guid>", "fullName": "Client Test", "phoneNumber": "77000002", "roles": ["Client"] }
}
// UserInfo (réponse de /me)
{ "id": "<guid>", "fullName": "Client Test", "phoneNumber": "77000002", "roles": ["Client"] }
```

**Claims du JWT** : `sub` (user id), `phone`, `fullName`, `role` (un par rôle), `jti`, `iat`.

## 8. Logique métier (handlers)

- **Register** : valide le rôle (∈ {Client, Driver, Admin}) ; crée l'`ApplicationUser` (`UserName = PhoneNumber`) via
  `UserManager.CreateAsync(user, password)` ; `AddToRoleAsync` ; en cas d'échec Identity → `Result.Failure` (Validation).
  Téléphone déjà pris → erreur Conflict.
- **Login** : `FindByNameAsync(phoneNumber)` ; vérifier lockout (`IsLockedOutAsync`) ;
  `SignInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)` ; si échec → `Error` générique
  (« Téléphone ou mot de passe incorrect », pas de fuite) ; récupérer rôles ; générer le JWT via `ITokenService`.
- **GetMe** : lit l'id du user depuis les claims (`ICurrentUser` ou `ClaimsPrincipal`), renvoie `UserInfo`.

Toutes les méthodes renvoient `Result<T>` ; mapping HTTP via `ResultExtensions` existant
(Validation→400, Conflict→409, et un mapping **401** pour les identifiants/lockout invalides — voir §10).

## 9. Validation

- `RegisterCommandValidator` : `FullName` non vide ; `PhoneNumber` non vide (format local) ; `Password` longueur min 6 ;
  `Role` ∈ {Client, Driver, Admin}.
- `LoginCommandValidator` : `PhoneNumber` et `Password` non vides.
- Exécutée par le décorateur de validation existant (FluentValidation).

## 10. Gestion d'erreurs

- Réutilise `Result<T>` / `Error` / `ErrorType`.
- **Nouveau besoin** : un `ErrorType.Unauthorized` → HTTP **401** pour identifiants invalides / compte verrouillé.
  Ajout d'une valeur `Unauthorized` à l'enum `ErrorType` (SharedKernel) et d'un arm dans `ResultExtensions`
  (`Unauthorized → 401`). C'est une petite extension cohérente du socle.

## 11. Stratégie de test

- **`TokenService`** : tests unitaires (le JWT généré contient les bons claims, expiration correcte, signé avec la clé).
- **Handlers `Register`/`Login`** : s'appuient sur `UserManager`/`SignInManager` (classes concrètes EF, difficiles à
  mocker proprement) → **vérification manuelle via Scalar** en Phase 1 ; **tests d'intégration reportés** (décision utilisateur).
- **Tests d'architecture** (NetArchTest) : étendre les règles à la couche Identity (Domain ne dépend pas d'Infrastructure, etc.).
- **Vérification manuelle** : register Client/Driver/Admin, login (bon/mauvais mot de passe → 401), `me` avec JWT.

## 12. Hors périmètre (Phase 1)

Refresh tokens & rotation (Phase 2), documents chauffeur & Blob (Phase 3), profil `Driver` (module Dispatch),
mise à jour du frontend React (chantier séparé après validation backend), OpenIdConnect / SSO, rate limiting.

## 13. Risques & points d'attention

- **Migration de schéma** : passage `EnsureCreated` → migrations. Comme la base de dev est un volume Aspire persistant
  qui a pu être créé par `EnsureCreated`, il faudra **supprimer le volume** (base vierge) avant la première migration
  pour éviter le conflit « objet zone_prices déjà existant ». À documenter dans le plan.
- **Ordre `base.OnModelCreating`** : appeler `base` AVANT `ApplyConfigurationsFromAssembly` pour ne pas écraser la config Identity.
- **Données legacy** : pas de reprise de données de l'ancien projet (bases distinctes) ; on repart propre.
