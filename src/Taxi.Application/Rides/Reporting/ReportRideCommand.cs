using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Reporting;

/// <summary>
/// Commande permettant à un client de signaler un problème survenu lors d'une course (motif obligatoire).
/// </summary>
public sealed record ReportRideCommand(int RideId, string ClientId, string Reason, string? Description) : ICommand<ReportDto>;
