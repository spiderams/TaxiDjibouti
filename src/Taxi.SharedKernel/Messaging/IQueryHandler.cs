using Taxi.SharedKernel;

namespace Taxi.SharedKernel.Messaging;

/// <summary>
/// Gère une requête et renvoie un <see cref="Result{TResponse}"/>.
/// </summary>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
