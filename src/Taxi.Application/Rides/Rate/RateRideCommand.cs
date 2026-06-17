using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Rate;

/// <summary>
/// Commande permettant à un client de noter une course terminée avec un score de 1 à 5 et un commentaire optionnel.
/// </summary>
public sealed record RateRideCommand(int RideId, string ClientId, int Score, string? Comment) : ICommand<RatingDto>;
