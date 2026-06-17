using Taxi.SharedKernel;

namespace Taxi.SharedKernel.Messaging;

/// <summary>
/// Gère une commande et renvoie un <see cref="Result{TResponse}"/>.
/// </summary>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}
