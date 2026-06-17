using Microsoft.Extensions.Logging;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Abstractions.Behaviors;

/// <summary>
/// Décorateurs transverses qui tracent automatiquement le cycle de vie de CHAQUE commande et requête
/// (début, succès, échec métier, exception). Branchés via Scrutor (<c>TryDecorate</c>) : aucun handler
/// n'a besoin d'écrire ces logs lui-même. S'appuie sur <see cref="RequestLog"/> (source-generated).
/// </summary>
public static class LoggingDecorator
{
    /// <summary>
    /// Enveloppe un <see cref="ICommandHandler{TCommand,TResponse}"/> pour le tracer.
    /// </summary>
    public sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var name = typeof(TCommand).Name;
            RequestLog.LogStarted(logger, name);
            try
            {
                var result = await inner.Handle(command, cancellationToken);
                if (result.IsSuccess)
                    RequestLog.LogSucceeded(logger, name);
                else
                    RequestLog.LogFailed(logger, name, result.Error.Code);
                return result;
            }
            catch (Exception ex)
            {
                RequestLog.LogException(logger, ex, name);
                throw;
            }
        }
    }

    /// <summary>
    /// Enveloppe un <see cref="IQueryHandler{TQuery,TResponse}"/> pour le tracer (symétrique des commandes).
    /// </summary>
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
                if (result.IsSuccess)
                    RequestLog.LogSucceeded(logger, name);
                else
                    RequestLog.LogFailed(logger, name, result.Error.Code);
                return result;
            }
            catch (Exception ex)
            {
                RequestLog.LogException(logger, ex, name);
                throw;
            }
        }
    }
}
