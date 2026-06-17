using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.DeclineOffer;

/// <summary>
/// Commande permettant au chauffeur de refuser une offre de course, déclenchant une nouvelle tentative de dispatch.
/// </summary>
public sealed record DeclineOfferCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
