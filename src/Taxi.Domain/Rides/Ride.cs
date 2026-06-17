using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

/// <summary>
/// Agrégat central représentant une course : porte les adresses de prise en charge et de destination,
/// les coordonnées GPS, le prix estimé, le chauffeur assigné et le statut qui pilote le cycle de vie complet
/// (Pending → Offered → Accepted → DriverArrived → InProgress → Completed / Cancelled).
/// </summary>
public sealed class Ride : Entity
{
    public string ClientId { get; private set; } = string.Empty;
    public int? DriverId { get; private set; }
    public string PickupAddress { get; private set; } = string.Empty;
    public string DestinationAddress { get; private set; } = string.Empty;
    public string PickupZone { get; private set; } = string.Empty;
    public string DestinationZone { get; private set; } = string.Empty;
    public double? PickupLatitude { get; private set; }
    public double? PickupLongitude { get; private set; }
    public double? DestinationLatitude { get; private set; }
    public double? DestinationLongitude { get; private set; }
    public decimal EstimatedPrice { get; private set; }
    public RideStatus Status { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int? OfferedDriverId { get; private set; }
    public DateTime? OfferExpiresAt { get; private set; }
    public List<int> TriedDriverIds { get; private set; } = [];

    private Ride() { } // EF

    /// <summary>
    /// Crée une nouvelle demande de course au statut <see cref="RideStatus.Pending"/>,
    /// prête à être affectée à un chauffeur disponible.
    /// </summary>
    public static Ride Request(
        string clientId, string pickupAddress, string destinationAddress,
        string pickupZone, string destinationZone,
        double? pickupLatitude, double? pickupLongitude,
        double? destinationLatitude, double? destinationLongitude,
        decimal estimatedPrice)
        => new()
        {
            ClientId = clientId,
            PickupAddress = pickupAddress,
            DestinationAddress = destinationAddress,
            PickupZone = pickupZone,
            DestinationZone = destinationZone,
            PickupLatitude = pickupLatitude,
            PickupLongitude = pickupLongitude,
            DestinationLatitude = destinationLatitude,
            DestinationLongitude = destinationLongitude,
            EstimatedPrice = estimatedPrice,
            Status = RideStatus.Pending
        };

    /// <summary>
    /// Acceptation directe par un chauffeur : la course passe de <see cref="RideStatus.Pending"/>
    /// à <see cref="RideStatus.Accepted"/> et enregistre l'heure d'acceptation.
    /// Échoue si la course n'est plus en attente.
    /// </summary>
    public Result Accept(int driverId)
    {
        if (Status != RideStatus.Pending)
            return Result.Failure(RideErrors.NotPending);

        DriverId = driverId;
        Status = RideStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Signale que le chauffeur est arrivé au point de prise en charge :
    /// fait passer la course de <see cref="RideStatus.Accepted"/> à <see cref="RideStatus.DriverArrived"/>.
    /// </summary>
    public Result MarkArrived()
    {
        if (Status != RideStatus.Accepted)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.DriverArrived;
        return Result.Success();
    }

    /// <summary>
    /// Démarre la course lorsque le client est à bord :
    /// fait passer la course de <see cref="RideStatus.DriverArrived"/> à <see cref="RideStatus.InProgress"/>.
    /// </summary>
    public Result Start()
    {
        if (Status != RideStatus.DriverArrived)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.InProgress;
        return Result.Success();
    }

    /// <summary>
    /// Clôture la course à l'arrivée à destination :
    /// fait passer la course de <see cref="RideStatus.InProgress"/> à <see cref="RideStatus.Completed"/>
    /// et enregistre l'heure de fin.
    /// </summary>
    public Result Complete()
    {
        if (Status != RideStatus.InProgress)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Annulation initiée par le client : autorisée uniquement si la course est encore
    /// en attente, offerte, acceptée ou si le chauffeur vient d'arriver.
    /// </summary>
    public Result CancelByClient()
    {
        if (Status is not (RideStatus.Pending or RideStatus.Offered or RideStatus.Accepted or RideStatus.DriverArrived))
            return Result.Failure(RideErrors.CannotCancel);

        Status = RideStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>
    /// Annulation initiée par le chauffeur : uniquement possible si la course est acceptée
    /// ou si le chauffeur est déjà arrivé sur place. La course retourne au pool pour être réattribuée.
    /// </summary>
    public Result CancelByDriver()
    {
        if (Status is not (RideStatus.Accepted or RideStatus.DriverArrived))
            return Result.Failure(RideErrors.CannotCancel);

        Status = RideStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>
    /// Propose la course à un chauffeur spécifique avec une fenêtre d'expiration :
    /// passe au statut <see cref="RideStatus.Offered"/> et bloque la course pour les autres chauffeurs
    /// le temps que l'offre soit acceptée ou expire.
    /// </summary>
    public Result Offer(int driverId, DateTime expiresAt)
    {
        if (Status != RideStatus.Pending)
            return Result.Failure(RideErrors.NotPending);

        Status = RideStatus.Offered;
        OfferedDriverId = driverId;
        OfferExpiresAt = expiresAt;
        return Result.Success();
    }

    /// <summary>
    /// Confirme l'acceptation d'une offre par le chauffeur ciblé :
    /// vérifie que le chauffeur est bien le destinataire de l'offre et que celle-ci n'a pas expiré,
    /// puis fait passer la course à <see cref="RideStatus.Accepted"/>.
    /// </summary>
    public Result AcceptOffer(int driverId)
    {
        if (Status != RideStatus.Offered)
            return Result.Failure(RideErrors.NotOffered);
        if (OfferedDriverId != driverId)
            return Result.Failure(RideErrors.OfferMismatch);
        if (OfferExpiresAt is null || OfferExpiresAt <= DateTime.UtcNow)
            return Result.Failure(RideErrors.OfferExpired);

        DriverId = driverId;
        Status = RideStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        OfferedDriverId = null;
        OfferExpiresAt = null;
        return Result.Success();
    }

    /// <summary>
    /// Remet la course au statut <see cref="RideStatus.Pending"/> lorsqu'une offre expire ou est refusée,
    /// permettant au système d'en proposer une nouvelle à un autre chauffeur.
    /// </summary>
    public Result ReturnToPending()
    {
        if (Status != RideStatus.Offered)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.Pending;
        OfferedDriverId = null;
        OfferExpiresAt = null;
        return Result.Success();
    }

    /// <summary>
    /// Enregistre qu'un chauffeur a déjà été sollicité pour cette course,
    /// afin d'éviter de lui reproposer la même offre lors des itérations suivantes.
    /// </summary>
    public void MarkDriverTried(int driverId)
    {
        if (!TriedDriverIds.Contains(driverId))
            TriedDriverIds.Add(driverId);
    }
}
