# Observabilité (logs source-generated) & lisibilité — Design

- **Version :** 1.0
- **Date :** 2026-06-17
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Inspiration :** EchoNG `ms-echo-core` (`LaunchDiffusionCommandHandler` — pattern `[LoggerMessage]` source-generated).

## 1. Objectif

Rendre l'application **traçable pour déboguer** : chaque commande/requête loggée automatiquement (début, succès,
échec, exception), via le pattern **`[LoggerMessage]` source-generated** (performant, structuré). Et **commenter**
le cœur architectural pour que le binôme comprenne l'objectif de chaque classe/fonction.

## 2. État actuel

- `LoggingDecorator.CommandHandler<,>` existe (commandes seulement) mais loggue en templates classiques
  (`logger.LogInformation("...{Command}", name)`), pas source-generated. **Les requêtes ne sont pas tracées.**
- `GlobalExceptionHandler` loggue les exceptions non gérées ; quelques logs ponctuels (`SignalRRealtimeNotifier`,
  background services).
- **`Serilog.AspNetCore` est déclaré dans `Directory.Packages.props` mais jamais branché** (aucun `UseSerilog`).
- Aspire `AddServiceDefaults` configure déjà **OpenTelemetry logging** → les logs remontent au **dashboard Aspire**.

## 3. Décisions validées

- **Backbone automatique + démo** : décorateurs source-generated pour commandes ET requêtes ; pattern `[LoggerMessage]`
  démontré sur 2 endroits métier (pas dans tous les handlers — YAGNI).
- **Sink = Aspire/OpenTelemetry** (déjà en place) ; on **retire** le package Serilog inutilisé.
- **Commentaires** : logging + liste bornée de classes architecturales (pas tout le code).

## 4. Catalogue de logs source-generated

**`Taxi.Application/Abstractions/Behaviors/RequestLog.cs`** — `internal static partial class RequestLog` :
```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Traitement de {RequestName}")]
public static partial void LogStarted(ILogger logger, string requestName);

[LoggerMessage(Level = LogLevel.Information, Message = "{RequestName} traitée avec succès")]
public static partial void LogSucceeded(ILogger logger, string requestName);

[LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} en échec : {ErrorCode}")]
public static partial void LogFailed(ILogger logger, string requestName, string errorCode);

[LoggerMessage(Level = LogLevel.Error, Message = "{RequestName} a levé une exception")]
public static partial void LogException(ILogger logger, Exception exception, string requestName);
```
Compile-time, structuré (`RequestName`/`ErrorCode` = propriétés structurées), zéro allocation si le niveau est désactivé.
Méthodes `public static` car appelées depuis les deux décorateurs (même assembly).

## 5. Décorateurs

**`LoggingDecorator.CommandHandler<,>`** (modifié) : remplacer les appels `logger.Log*` par
`RequestLog.LogStarted/LogSucceeded/LogFailed/LogException`. Comportement inchangé (pass-through + rethrow).

**`LoggingDecorator.QueryHandler<,>`** (nouveau) : symétrique pour `IQueryHandler<TQuery, TResponse>` :
```csharp
public sealed class QueryHandler<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    ILogger<QueryHandler<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
    {
        var name = typeof(TQuery).Name;
        RequestLog.LogStarted(logger, name);
        try
        {
            var result = await inner.Handle(query, cancellationToken);
            if (result.IsSuccess) RequestLog.LogSucceeded(logger, name);
            else RequestLog.LogFailed(logger, name, result.Error.Code);
            return result;
        }
        catch (Exception ex)
        {
            RequestLog.LogException(logger, ex, name);
            throw;
        }
    }
}
```

**Enregistrement** (`Taxi.Application/DependencyInjection.cs`, `AddApplication`) : ajouter, à côté du
`TryDecorate` du command handler, le `TryDecorate` du query handler (open generic) :
```csharp
services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));
```
(Le `TryDecorate` ne plante pas si aucun handler n'est encore enregistré — cf. décision foundation.)

→ Toute commande et toute requête est tracée automatiquement.

## 6. Démonstration du pattern `[LoggerMessage]` (modèle binôme)

**`RideDispatcher`** (devient `internal sealed partial class`) — méthodes privées `[LoggerMessage]` :
- `LogOfferMade` (Information) : "Course {RideId} offerte au chauffeur {DriverId} (expire {ExpiresAt})" — après `Offer`.
- `LogNoCandidate` (Warning) : "Aucun chauffeur disponible pour la course {RideId} → retour en attente" — branche sans candidat.
- `LogNoCoordinates` (Information) : "Course {RideId} sans coordonnées → flux manuel" — branche sans coords.

**`OfferTimeoutService`** (devient `internal sealed partial class`) — méthode `[LoggerMessage]` :
- `LogOffersExpired` (Information) : "{Count} offre(s) expirée(s) réattribuée(s)" — après la boucle (si `count > 0`),
  avec garde `if (count > 0)` (le `IsEnabled` n'est pas requis ici car pas de string coûteux).

Ces deux fichiers servent de **référence commentée** pour le binôme (le commentaire explique le « pourquoi » du pattern).

## 7. Sink (Aspire/OpenTelemetry)

- **Retirer** la ligne `<PackageVersion Include="Serilog.AspNetCore" ... />` de `Directory.Packages.props`, et toute
  `<PackageReference Include="Serilog.AspNetCore" />` dans un `.csproj` (vérifier `Taxi.Web.Api.csproj`).
- **`appsettings.json`** (Web.Api) : section `Logging:LogLevel` cohérente — `Default: Information`,
  `Microsoft.AspNetCore: Warning`, `Microsoft.EntityFrameworkCore: Warning` (réduire le bruit SQL).
- Aucun câblage de sink à ajouter : `AddServiceDefaults` (OTel) capte déjà les logs → dashboard Aspire.

## 8. Commentaires (XML `///`, en français) — liste bornée

Objectif par classe : **quoi / pourquoi / comment l'utiliser**. Fichiers :

**SharedKernel :** `Result.cs` (Result/Result<T>), `Error.cs` (Error/ErrorType), `Entity.cs`,
`Messaging/ICommand.cs`, `Messaging/IQuery.cs`, `Messaging/ICommandHandler.cs`, `Messaging/IQueryHandler.cs`.

**Application :** `Abstractions/IRepository.cs`, `Abstractions/Behaviors/LoggingDecorator.cs` (commande + query),
`Abstractions/Behaviors/ValidationDecorator.cs`, `Abstractions/Behaviors/RequestLog.cs`,
`Dispatch/RideDispatcher.cs`, `Dispatch/IDriverLocator.cs`, `Realtime/IRealtimeNotifier.cs`,
`Rides/Request/RequestRideCommandHandler.cs` (handler représentatif).

**Web.Api :** `Endpoints/IEndpoint.cs`, `Endpoints/ResultExtensions.cs`, `Realtime/RideHub.cs`,
`Middleware/GlobalExceptionHandler.cs`, `Middleware/SecurityHeadersMiddleware.cs`.

Style : `/// <summary>` concis (1-3 phrases), + `/// <param>`/`/// <returns>` sur les méthodes publiques non triviales.
Pas de commentaire sur du code évident (getters, ctors triviaux). Les commentaires expliquent l'**intention**, pas la syntaxe.

## 9. Stratégie de test

- **`LoggingDecorator.QueryHandler`** (unit) : un handler interne factice → le décorateur renvoie le résultat succès
  inchangé ; sur exception du handler interne, le décorateur **relance** (vérifie le pass-through, pas le log lui-même).
- **Logs source-generated** : non testés unitairement (mécanisme du framework). Build = la génération de source réussit.
- **Vérification manuelle (dashboard Aspire)** : lancer une requête (ex. `GET /api/dispatch/nearest-drivers` ou un login)
  → voir `Traitement de ...` puis `... traitée avec succès` ; provoquer une erreur (mauvais token / `/_debug` retiré →
  plutôt une commande en échec métier) → voir le log `Warning` avec le code d'erreur ; demander une course avec coords
  → voir le log métier `Course {RideId} offerte au chauffeur {DriverId}` du `RideDispatcher`.

## 10. Risques & points d'attention

- **`TryDecorate` query** : doit utiliser `TryDecorate` (pas `Decorate`) pour ne pas planter si l'ordre
  d'enregistrement laisse temporairement zéro query handler (leçon de la foundation).
- **Classe `partial`** : `RideDispatcher` et `OfferTimeoutService` doivent passer `partial` pour héberger les
  méthodes `[LoggerMessage]` générées. Ne change pas leur visibilité (`internal sealed`).
- **Double logging** : le décorateur loggue déjà le cycle commande/requête ; les logs métier (section 6) doivent
  apporter de l'info **complémentaire** (le « quoi » métier), pas redire « started/succeeded ».
- **Retrait de Serilog** : vérifier qu'aucun `using Serilog;` ni `UseSerilog(...)` ne subsiste (le grep initial n'en a
  trouvé aucun, mais revalider après retrait du package).
- **Performance des gardes** : suivre EchoNG — `if (logger.IsEnabled(LogLevel.X))` seulement quand le message implique
  une construction coûteuse (`string.Join`, concat). Pour des champs simples, l'appel direct suffit (la source-gen
  fait elle-même le court-circuit de niveau).

## 11. Hors périmètre

Logs métier dans tous les handlers, Serilog/Seq/fichier, trace-id/corrélation custom (OTel le fournit déjà),
métriques/alerting, commentaires exhaustifs sur tout le code.
