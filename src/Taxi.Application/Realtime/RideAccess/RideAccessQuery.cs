using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.RideAccess;

/// <summary>
/// Requête vérifiant si un utilisateur est autorisé à rejoindre le hub temps réel d'une course donnée.
/// </summary>
public sealed record RideAccessQuery(int RideId, string UserId, bool IsAdmin) : IQuery<bool>;
