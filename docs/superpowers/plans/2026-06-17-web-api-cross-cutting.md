# Module transverse Web.Api — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter un filet anti-exception (ProblemDetails 500 propre) et les headers de sécurité OWASP à la couche Web.Api.

**Architecture:** Nouveau dossier `src/Taxi.Web.Api/Middleware/`. `GlobalExceptionHandler` (idiome .NET 10 `IExceptionHandler` + `AddProblemDetails`) capte uniquement l'inattendu (les erreurs métier restent gérées par le pattern Result + `ToHttpResult`). `SecurityHeadersMiddleware` pose les headers OWASP en excluant `/scalar`, `/openapi`, `/health`. Câblage dans `Program.cs`.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, `IExceptionHandler`, `ProblemDetails`. Pas de migration, pas d'entité, pas de test automatisé (vérif manuelle).

**Spec :** `docs/superpowers/specs/2026-06-17-web-api-cross-cutting-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). 60 tests verts au départ.

> **Portée :** 2 fichiers créés + `Program.cs` modifié. Pas de `ValidationExceptionHandler` (couvert par le décorateur), pas de trace-logging (Aspire ServiceDefaults le fournit).

---

## Structure de fichiers cible

```
src/Taxi.Web.Api/Middleware/GlobalExceptionHandler.cs      — créé
src/Taxi.Web.Api/Middleware/SecurityHeadersMiddleware.cs   — créé
src/Taxi.Web.Api/Program.cs                                — modifié (DI + pipeline)
```

---

## Task 1: GlobalExceptionHandler

**Files:**
- Create: `src/Taxi.Web.Api/Middleware/GlobalExceptionHandler.cs`
- Modify: `src/Taxi.Web.Api/Program.cs`

- [ ] **Step 1: Create `GlobalExceptionHandler.cs`**
```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Taxi.Web.Api.Middleware;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception occurred");

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
            Title = "Server failure",
            Detail = environment.IsDevelopment()
                ? exception.Message
                : "Une erreur interne est survenue."
        };

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
```
NOTE: `IExceptionHandler` est dans `Microsoft.AspNetCore.Diagnostics` ; `ProblemDetails` dans `Microsoft.AspNetCore.Mvc`. `ILogger<T>`/`IHostEnvironment` sont dans les global usings ASP.NET. Retourner `true` = exception gérée (pas de re-throw).

- [ ] **Step 2: Register in `Program.cs`** — après la ligne `builder.Services.AddOpenApi(...)` (vers la ligne 14), ajouter :
```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
```

- [ ] **Step 3: Wire the pipeline in `Program.cs`** — juste après `var app = builder.Build();` (avant le bloc `using (var scope ...)`), ajouter en **tout premier** du pipeline :
```csharp
app.UseExceptionHandler();
```
Et ajouter le `using` en tête de `Program.cs` si absent :
```csharp
using Taxi.Web.Api.Middleware;
```

- [ ] **Step 4: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): global exception handler (ProblemDetails 500, detail hidden in prod)"
```

---

## Task 2: SecurityHeadersMiddleware

**Files:**
- Create: `src/Taxi.Web.Api/Middleware/SecurityHeadersMiddleware.cs`
- Modify: `src/Taxi.Web.Api/Program.cs`

- [ ] **Step 1: Create `SecurityHeadersMiddleware.cs`**
```csharp
namespace Taxi.Web.Api.Middleware;

/// <summary>
///     Pose les headers HTTP de sécurité recommandés par OWASP
///     (clickjacking, MIME sniffing, XSS, CSP). Réf : OWASP A05:2021.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        // Exclut la doc/UI : la CSP 'default-src self' bloquerait le CDN Scalar.
        var path = httpContext.Request.Path.Value;
        if (path is not null && (
            path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)))
        {
            await next(httpContext);
            return;
        }

        var headers = httpContext.Response.Headers;
        headers["X-Frame-Options"] = "DENY";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-XSS-Protection"] = "1; mode=block";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        await next(httpContext);
    }
}
```
NOTE: `RequestDelegate`/`HttpContext` sont dans les global usings ASP.NET. Classe `public` (convention middleware conventionnel via `UseMiddleware<>`).

- [ ] **Step 2: Wire the pipeline in `Program.cs`** — juste APRÈS `app.UseExceptionHandler();` (ajouté en Task 1), ajouter :
```csharp
app.UseMiddleware<SecurityHeadersMiddleware>();
```

- [ ] **Step 3: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors ; les 60 tests passent toujours (non-régression — aucun test ne dépend du pipeline).

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): OWASP security headers middleware (excl. scalar/openapi/health)"
```

---

## Task 3: Vérification manuelle

> Pas de migration / pas de reset de volume. Démarrer l'AppHost (F5 sur Taxi.AppHost dans VS, ou `dotnet run`).

- [ ] **Step 1: Headers présents sur l'API**
Dans Scalar (ou via les DevTools réseau du navigateur), appeler un endpoint API, ex. `GET /api/pricing/estimate?...` ou `GET /api/admin/stats`.
Vérifier dans la réponse la présence de : `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy`, `Referrer-Policy`, `Permissions-Policy`.

- [ ] **Step 2: Scalar non bloqué**
Ouvrir `/scalar/v1` (ou l'URL Scalar affichée) : l'UI se charge normalement (le CDN n'est PAS bloqué par la CSP) et la réponse de cette page **n'a pas** le header CSP.

- [ ] **Step 3: Filet anti-exception (endpoint temporaire « boom »)**
Ajouter TEMPORAIREMENT dans `Program.cs`, juste avant `app.Run();` :
```csharp
app.MapGet("/_debug/boom", () => { throw new Exception("boom"); });
```
Démarrer, appeler `GET /_debug/boom` → **500** avec un corps `ProblemDetails` JSON propre :
`title = "Server failure"`, `detail = "boom"` (car on est en Development).
**Puis SUPPRIMER cette ligne `/_debug/boom`** (ne doit pas rester dans le code). Rebuild.

- [ ] **Step 4: Non-régression du pattern Result**
`GET /api/admin/stats` sans token Admin → **403** (et non 500) ; une ressource inexistante (ex. `GET /api/rides/me` côté droit, ou une course inconnue) → **404/400** selon le cas.
Confirme que le `GlobalExceptionHandler` ne capte PAS les erreurs métier (toujours gérées par `ToHttpResult`).

- [ ] **Step 5: Confirmer les résultats à l'utilisateur.** Aucun commit (vérification). Si l'endpoint « boom » a été ajouté/retiré, vérifier `git status` propre.

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : les 60 tests toujours verts.
- [ ] Les headers OWASP apparaissent sur les réponses API ; Scalar fonctionne toujours.
- [ ] Une exception non gérée renvoie un `ProblemDetails` 500 propre (détail en dev, générique en prod).
- [ ] Les erreurs métier renvoient toujours leur bon code via `ToHttpResult` (pas de régression).
- [ ] Aucun endpoint `/_debug/boom` résiduel. Tout committé sur `main`.

## Suite (hors périmètre)

CORS, rate limiting, `ValidationExceptionHandler` (inutile : pattern Result), frontend, SignalR, Dispatch.
