namespace Taxi.SharedKernel.Messaging;

/// <summary>
/// Marqueur d'une commande CQRS (opération d'écriture) renvoyant <c>TResponse</c>.
/// </summary>
public interface ICommand<TResponse>;
