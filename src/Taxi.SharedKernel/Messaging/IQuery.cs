namespace Taxi.SharedKernel.Messaging;

/// <summary>
/// Marqueur d'une requête CQRS (lecture seule) renvoyant <c>TResponse</c>.
/// </summary>
public interface IQuery<TResponse>;
