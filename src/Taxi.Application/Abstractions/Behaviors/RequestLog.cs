using Microsoft.Extensions.Logging;

namespace Taxi.Application.Abstractions.Behaviors;

/// <summary>
/// Catalogue centralisé des messages de log du cycle de vie des requêtes (commandes et requêtes CQRS).
/// Utilise les générateurs de source de Microsoft.Extensions.Logging (<c>[LoggerMessage]</c>) :
/// les méthodes sont générées à la compilation, sans allocation quand le niveau est désactivé,
/// et les paramètres ({RequestName}, {ErrorCode}) deviennent des propriétés structurées exploitables
/// dans le dashboard Aspire / OpenTelemetry.
/// </summary>
internal static partial class RequestLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Traitement de {RequestName}")]
    public static partial void LogStarted(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{RequestName} traitée avec succès")]
    public static partial void LogSucceeded(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} en échec : {ErrorCode}")]
    public static partial void LogFailed(ILogger logger, string requestName, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "{RequestName} a levé une exception")]
    public static partial void LogException(ILogger logger, Exception exception, string requestName);
}
