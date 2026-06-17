# Module Identité — Phase 2 (Refresh tokens) — Design

- **Version :** 1.0
- **Date :** 2026-06-16
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Spec parent :** `docs/superpowers/specs/2026-06-16-identity-module-design.md` (Phase 1 = auth de base, déjà livrée)
- **Référence de pattern :** Skoleo / IqraInstitut (refresh tokens avec rotation + détection de réutilisation)

## 1. Objectif

Ajouter à l'authentification des **refresh tokens** avec **rotation** et **détection de réutilisation**, plus
un endpoint de **logout** (`revoke`) et un **service de nettoyage**. Cela permet des access tokens courts
(60 min) renouvelables sans re-saisie du mot de passe, tout en détectant le vol d'un refresh token.

## 2. Décisions validées

- **Scope A** : cœur sécurisé complet — rotation + détection de réutilisation (`FamilyId`) + `/refresh` + `/revoke` + service de nettoyage.
- **Refresh token** : 64 octets aléatoires → base64 (le « raw », renvoyé au client) ; **stocké uniquement haché en SHA256** (jamais en clair).
- **`AuthResponse` évolue** : ajout de `RefreshToken` (raw) + `RefreshTokenExpiresAt`. Register ET login émettent désormais aussi un refresh token.
- **Repository générique** `IRepository<RefreshToken>` + **Specifications** (pas de `IRefreshTokenRepository` dédié) — cohérent avec le module Tarification.
- **Pas d'audit IP/User-Agent** (YAGNI — passe de durcissement ultérieure possible).
- Frontend (`api.ts` : lire `refreshToken`, appeler `/refresh`) = chantier séparé, hors périmètre.

## 3. Entité `RefreshToken` (Domain/Identity)

`RefreshToken : Entity` (base SharedKernel : `int Id`, `CreatedAt`), **entité riche** (setters privés, factory, méthode de révocation) :

| Propriété | Type | Rôle |
|-----------|------|------|
| `UserId` | string | FK vers `ApplicationUser` |
| `TokenHash` | string | SHA256 (hex) du raw token |
| `ExpiresAt` | DateTime | expiration |
| `IsRevoked` | bool | révoqué ? |
| `RevokedAt` | DateTime? | date de révocation |
| `RevokedReason` | string? | "Rotation" / "Logout" / "TokenReuse" |
| `FamilyId` | Guid | identifiant de la chaîne de rotation |
| `ReplacedByTokenId` | int? | token qui l'a remplacé (rotation) |

API du domaine :
- `static RefreshToken Create(string userId, string tokenHash, DateTime expiresAt, Guid familyId)`
- `void Revoke(string reason, int? replacedByTokenId = null)`
- `bool IsActive => !IsRevoked && ExpiresAt > DateTime.UtcNow`

## 4. Génération & hachage (`ITokenService` étendu)

Ajout à `ITokenService` :
- `string GenerateRefreshToken()` — 64 octets via `RandomNumberGenerator` → `Convert.ToBase64String`.
- `string HashRefreshToken(string rawToken)` — `SHA256.HashData(UTF8(raw))` → hex minuscule.

Le raw token est renvoyé au client ; seul le hash est persisté et recherché.

## 5. Contrat (`AuthResponse`)

```jsonc
{
  "accessToken": "<jwt>",
  "expiresAt": "...",            // expiration access token
  "tokenType": "Bearer",
  "refreshToken": "<base64>",    // NOUVEAU (raw, à stocker côté client)
  "refreshTokenExpiresAt": "...",// NOUVEAU
  "user": { "id": "...", "fullName": "...", "phoneNumber": "...", "roles": [...] }
}
```

`POST /api/auth/refresh` renvoie le même `AuthResponse` (nouveaux access + refresh).

## 6. Endpoints (convention enrichie : WithName/WithTags(Tags.Identity)/WithSummary/WithDescription)

| Endpoint | Auth | Entrée | Sortie |
|----------|------|--------|--------|
| `POST /api/auth/refresh` | anonyme | `{ refreshToken }` | `AuthResponse` (rotation) |
| `POST /api/auth/revoke` | JWT | `{ refreshToken }` | `204 No Content` |

## 7. Logique de rotation et détection de réutilisation (`RefreshTokenCommandHandler`)

1. Hacher le raw token reçu ; chercher le `RefreshToken` par hash (`RefreshTokenByHashSpec`).
2. Introuvable → `Error.Unauthorized("Auth.InvalidToken", ...)`.
3. **Déjà révoqué = réutilisation détectée** → révoquer **toute la famille** (`RefreshTokenByFamilySpec`, raison "TokenReuse") → `Error.Unauthorized("Auth.TokenReuse", ...)`.
4. Expiré (`!IsActive` pour cause d'expiration) → `Error.Unauthorized("Auth.ExpiredToken", ...)`.
5. Charger l'utilisateur (`UserManager.FindByIdAsync(UserId)`) + ses rôles.
6. Générer **nouveau** refresh token (même `FamilyId`), le persister ; révoquer l'ancien (`Revoke("Rotation", newToken.Id)`).
7. Émettre un nouvel access token ; renvoyer `AuthResponse`.

`RevokeTokenCommandHandler` : hacher, charger par hash, `Revoke("Logout")`, `SaveChanges` → `Result.Success()`. (Idempotent : token introuvable ou déjà révoqué → succès silencieux pour ne pas fuir d'info.)

## 8. Émission à l'inscription/connexion

Les handlers `Register` et `Login` (Phase 1) sont **étendus** : après l'access token, ils créent un `RefreshToken`
(nouvelle `FamilyId`), le persistent via `IRepository<RefreshToken>`, et incluent le raw + son expiration dans `AuthResponse`.
Pour éviter la duplication, une petite brique partagée (méthode privée ou service applicatif interne `AuthTokenIssuer`)
construit l'`AuthResponse` complet (access + refresh persisté) à partir d'un `ApplicationUser` + rôles.

## 9. Repository & Specifications

- `IRepository<RefreshToken>` (générique, déjà disponible — enregistré par `AddInfrastructure`).
- `RefreshTokenByHashSpec(string hash)` : `Where(t => t.TokenHash == hash)`.
- `RefreshTokenByFamilySpec(Guid familyId)` : `Where(t => t.FamilyId == familyId)`.
- Révocation de famille : charger via la spec, révoquer chaque token actif, `SaveChangesAsync`.
- EF config `RefreshTokenConfiguration` (`IEntityTypeConfiguration`) : table `refresh_tokens`, index sur `token_hash` et `family_id`, longueurs de colonnes.

## 10. Service de nettoyage

`RefreshTokenCleanupService : BackgroundService` (Infrastructure) : boucle toutes les **24 h**, supprime les tokens
dont `ExpiresAt < now` OU (`IsRevoked` ET `RevokedAt < now - 7j`). Crée son propre scope DI pour résoudre le `AppDbContext`.
Enregistré via `services.AddHostedService<RefreshTokenCleanupService>()` dans `AddIdentityInfrastructure`.

## 11. Configuration

`JwtSettings` gagne `RefreshTokenLifetimeDays` (défaut 7). `appsettings.json` : ajout de la clé sous `Jwt`.

## 12. Migration

Migration EF `AddRefreshTokens` → table `refresh_tokens` (toutes colonnes snake_case, index uniques/non-uniques sur
`token_hash` et `family_id`). Appliquée automatiquement au démarrage par le `MigrateAsync` existant (Phase 1).
Pas de wipe de volume nécessaire (on ajoute une migration par-dessus l'existant).

## 13. Stratégie de test

- **Unitaires** : `ITokenService.GenerateRefreshToken` (non vide, longueur), `HashRefreshToken` (déterministe, hex) ; entité `RefreshToken` (`Create`, `Revoke`, `IsActive`).
- **Unitaires handler** : `RefreshTokenCommandHandler` est testable (mock `IRepository<RefreshToken>`, `ITokenService`, et `UserManager` — la partie UserManager reste difficile à mocker ; on teste au minimum les branches introuvable / réutilisation / expiré via le repository mocké).
- **Tests d'archi** : inchangés (RefreshToken en Domain).
- **Vérification manuelle (Scalar)** : login → récupérer refreshToken → `/refresh` (rotation, nouveau token) → rejouer l'ancien refreshToken → 401 + famille révoquée → `/revoke` (logout) → 204.
- Tests d'intégration : reportés (décision utilisateur).

## 14. Hors périmètre

Audit IP/User-Agent, « déconnecter tous les appareils », rotation côté SignalR, documents chauffeur (Phase 3),
mise à jour du frontend, rate limiting sur `/refresh`.

## 15. Risques & points d'attention

- **Concurrence sur le refresh** : deux refresh simultanés avec le même token → l'un réussit, l'autre voit le token déjà
  révoqué et déclenche la détection de réutilisation (révocation de famille). Comportement acceptable et sûr en Phase 2
  (pas de fenêtre de grâce). À documenter.
- **`AuthResponse` modifié** : tout consommateur (frontend) doit gérer le nouveau champ ; le frontend est hors périmètre
  mais la rupture de contrat est à noter.
- **Nettoyage** : le `BackgroundService` doit créer un scope DI (le `AppDbContext` est scoped) et logguer ses passages.
