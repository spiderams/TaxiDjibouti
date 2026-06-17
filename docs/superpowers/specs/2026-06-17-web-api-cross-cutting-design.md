# Module transverse Web.Api (anti-exception + headers OWASP) — Design

- **Version :** 1.0
- **Date :** 2026-06-17
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Inspiration :** `Skoleo.WebApi/Middleware` (`GlobalExceptionHandler`, `SecurityHeadersMiddleware`)

## 1. Objectif

Combler les deux seuls vrais manques de la couche Web.Api par rapport à Skoleo :

1. **Filet anti-exception** — aujourd'hui `Program.cs` n'a aucun `UseExceptionHandler`. Une exception
   *inattendue* (bug, NullRef, panne DB) remonte brute (stack trace exposée). Il faut un handler centralisé
   qui renvoie un `ProblemDetails` 500 propre.
2. **Headers de sécurité OWASP** — l'API ne pose aucun header de sécurité (`X-Frame-Options`, CSP, etc.).

## 2. Décisions validées

- **Pas de remplacement du pattern Result.** Les erreurs *métier* restent des valeurs (`Result.Failure`)
  converties par `ToHttpResult()` (400/401/403/404/409). Le `GlobalExceptionHandler` n'attrape **que**
  l'inattendu (500). C'est la différence fondamentale avec Skoleo (qui, lui, lève des exceptions métier).
- **Idiome .NET 10** pour les exceptions : `IExceptionHandler` + `AddProblemDetails()` + `UseExceptionHandler()`
  (pas de classe middleware manuelle pour ce point).
- **Headers : set OWASP complet** (comme Skoleo), CSP incluse, avec exclusion des chemins UI/doc.
- **Détail d'exception** : exposé en `Development`, masqué en `Production`.
- **Nouveau dossier** `src/Taxi.Web.Api/Middleware/` (répond à l'observation « Skoleo a un folder Middleware »).
- **Hors périmètre** : `ValidationExceptionHandler` (couvert par le `ValidationDecorator` + Result),
  `LogContextTraceLoggingMiddleware` (couvert par Aspire `ServiceDefaults` / OpenTelemetry).

## 3. Composant 1 — GlobalExceptionHandler

**Fichier :** `src/Taxi.Web.Api/Middleware/GlobalExceptionHandler.cs`

- `internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler`.
- `TryHandleAsync(HttpContext, Exception, CancellationToken)` :
  - `logger.LogError(exception, "Unhandled exception")`.
  - Construit un `ProblemDetails` :
    - `Status = 500`, `Title = "Server failure"`, `Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1"`.
    - `Detail` = `exception.Message` **si** `env.IsDevelopment()`, sinon `"Une erreur interne est survenue."`.
  - Écrit la réponse (`httpContext.Response.StatusCode = 500` + `WriteAsJsonAsync`).
  - `return true` (exception gérée).

**Câblage `Program.cs`** (dans `builder.Services`) :
```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
```
**Pipeline** : `app.UseExceptionHandler();` en **tête** du pipeline (avant tout le reste).

## 4. Composant 2 — SecurityHeadersMiddleware

**Fichier :** `src/Taxi.Web.Api/Middleware/SecurityHeadersMiddleware.cs`

- `public sealed class SecurityHeadersMiddleware(RequestDelegate next)` avec `InvokeAsync(HttpContext)`.
- **Exclusion** : si le chemin commence par `/scalar`, `/openapi` ou `/health` (insensible à la casse),
  on appelle `next` sans poser de headers (sinon la CSP bloque le CDN Scalar en dev).
- Sinon, pose sur `Response.Headers` :

| Header | Valeur |
|--------|--------|
| `X-Frame-Options` | `DENY` |
| `X-Content-Type-Options` | `nosniff` |
| `X-XSS-Protection` | `1; mode=block` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Content-Security-Policy` | `default-src 'self'; frame-ancestors 'none'` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=()` |

- Puis `await next(httpContext)`.

**Câblage `Program.cs`** : `app.UseMiddleware<SecurityHeadersMiddleware>();`.

## 5. Ordre du pipeline (Program.cs)

```
app.UseExceptionHandler();                 // 1. filet anti-exception (tout en haut)
app.UseMiddleware<SecurityHeadersMiddleware>();  // 2. headers de sécurité
app.MapDefaultEndpoints();                 // (health Aspire — déjà présent)
if (Development) { MapOpenApi(); MapScalarApiReference(); }
app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints();
```

> `UseExceptionHandler` doit être le premier pour englober tout ce qui suit. Le middleware de headers vient
> juste après pour couvrir aussi les réponses d'erreur.

## 6. Stratégie de test

Un middleware se teste mal en unitaire isolé → **vérification manuelle (Scalar / curl)** :

1. **Headers présents** : `GET /api/admin/stats` (ou tout endpoint API) → la réponse contient
   `X-Frame-Options`, `X-Content-Type-Options`, `Content-Security-Policy`, etc.
2. **Exclusion UI** : `GET /scalar/...` s'affiche correctement (CDN non bloqué) et **n'a pas** la CSP.
3. **Filet anti-exception** : un endpoint de test temporaire qui `throw new Exception("boom")` →
   réponse **500 `ProblemDetails`** propre, `detail = "boom"` en Development (et générique en Production).
   *(L'endpoint de test est retiré après vérification — il ne reste pas dans le code.)*
4. **Non-régression** : une erreur métier (ex. `GET /api/admin/*` sans token Admin → 403, course inexistante → 404)
   renvoie **toujours** le bon code via `ToHttpResult` (le handler d'exception ne s'en mêle pas).

## 7. Hors périmètre

- `ValidationExceptionHandler` (le `ValidationDecorator` renvoie déjà `Result.Failure(Validation)` → 400).
- Logging de trace/corrélation manuel (`ServiceDefaults` Aspire le fournit).
- Rate limiting, CORS (non requis pour l'instant — YAGNI).
- Tests automatisés du pipeline (vérification manuelle suffisante pour ce module transverse).

## 8. Risques & points d'attention

- **Ordre du pipeline** : `UseExceptionHandler` doit précéder tout le reste, sinon une exception levée en amont
  n'est pas captée.
- **CSP vs Scalar** : sans l'exclusion `/scalar` + `/openapi`, la CSP `default-src 'self'` bloque le CDN Scalar
  → l'UI ne se charge plus en dev. L'exclusion est obligatoire.
- **Endpoint de test « boom »** : strictement temporaire, à supprimer avant commit final (ne doit pas exposer
  un crash volontaire en production).
