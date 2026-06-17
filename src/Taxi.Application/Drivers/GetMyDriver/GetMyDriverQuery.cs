using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.GetMyDriver;

/// <summary>
/// Requête pour récupérer le profil chauffeur de l'utilisateur connecté à partir de son identifiant.
/// </summary>
public sealed record GetMyDriverQuery(string UserId) : IQuery<DriverDto>;
