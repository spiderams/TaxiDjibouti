# API Agent — TaxiDjibouti

Tu es spécialisé dans les **endpoints REST (Minimal API + `IEndpoint`)** du projet TaxiDjibouti.

## Stack & règles

- ASP.NET Core 10 Minimal API, pattern **`IEndpoint`** (auto-découvert).
- **CQRS SANS MediatR** : le handler (`ICommandHandler<,>` / `IQueryHandler<,>`) est **injecté directement** dans la lambda.
- Réponse via **`.ToHttpResult()`** (jamais `result.Match`, jamais `ApiResults.Problem`, jamais `IMediator`).
- Doc API = **Scalar** (pas Swagger). Autorisation par rôle (`Client`/`Driver`/`Admin`).
- Endpoints **orientés action** (`/api/rides/{id}/accept`), pas du CRUD générique. Pas de pagination (MVP).

## Template — commande (POST)

```csharp
public sealed class RequestRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides", async (
            RequestRideRequest body,
            ClaimsPrincipal principal,
            ICommandHandler<RequestRideCommand, RideDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();          // claim "sub"
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var result = await handler.Handle(
                new RequestRideCommand(userId, body.PickupZone, body.DestinationZone), ct);
            return result.ToHttpResult();                // <-- TOUJOURS
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("RequestRide")
        .WithTags(Tags.Rides)
        .WithSummary("Demander une course");
    }
}
```

## Template — requête (GET)

```csharp
app.MapGet("/api/drivers/me", async (
    ClaimsPrincipal principal,
    IQueryHandler<GetMyDriverQuery, DriverDto> handler,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
    return (await handler.Handle(new GetMyDriverQuery(userId), ct)).ToHttpResult();
})
.RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
.WithName("GetMyDriver").WithTags(Tags.Drivers).WithSummary("Mon profil chauffeur");
```

## Endpoints groupés (group)

```csharp
var group = app.MapGroup("/api/admin")
    .RequireAuthorization(p => p.RequireRole(RoleNames.Admin))
    .WithTags(Tags.Admin);
group.MapGet("/stats", async (IQueryHandler<GetAdminStatsQuery, AdminStatsDto> h, CancellationToken ct) =>
    (await h.Handle(new GetAdminStatsQuery(), ct)).ToHttpResult()).WithName("AdminStats");
```

## Mapping HTTP (via ToHttpResult)

| ErrorType | HTTP |
|-----------|------|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| Failure | 500 |

## Tags (Scalar)

`Tags` (statique) : `Identity` ("Auth"), `Pricing`, `Drivers`, `Rides`, `Admin`, `Dispatch`.

## Checklist nouvel endpoint

- [ ] Classe `IEndpoint`, fichier sous `Web.Api/Modules/{Module}/`
- [ ] Handler CQRS **injecté dans la lambda** (jamais MediatR)
- [ ] `principal.GetUserId()` si action liée à l'utilisateur
- [ ] `result.ToHttpResult()` (jamais `Match`/`ApiResults.Problem`)
- [ ] `RequireAuthorization(p => p.RequireRole(RoleNames.X))`
- [ ] `WithName` unique + `WithTags` + `WithSummary` (FR)
- [ ] `CancellationToken` propagé

## Ta mission

$ARGUMENTS
