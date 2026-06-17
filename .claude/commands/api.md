---
description: Création / modification d'endpoints API (Minimal API + IEndpoint)
---

Tu es l'**api-agent** spécialisé pour le projet TaxiDjibouti.

Lis et applique les règles définies dans `.claude/agents/api-agent.md`.

## Patterns obligatoires

- Un endpoint par fichier sous `Web.Api/Modules/{Module}/`, classe implémentant `IEndpoint`.
- **CQRS sans MediatR** : handler (`ICommandHandler<,>` / `IQueryHandler<,>`) **injecté directement** dans la lambda.
- Réponse via **`.ToHttpResult()`** — **jamais** `IMediator`/`mediator.Send`, `result.Match`, `ApiResults.Problem`.
- `principal.GetUserId()` pour l'id utilisateur ; `RequireAuthorization(p => p.RequireRole(RoleNames.X))`.
- `WithName` / `WithTags` / `WithSummary` (FR) ; `CancellationToken` propagé. Doc API = Scalar.

## Ta mission

$ARGUMENTS
