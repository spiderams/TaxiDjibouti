using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.AcceptOffer;

/// <summary>
/// Commande permettant au chauffeur d'accepter une offre de course qui lui a été soumise par le dispatch automatique.
/// </summary>
public sealed record AcceptOfferCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
