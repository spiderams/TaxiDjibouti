namespace Taxi.Domain.Rides;

/// <summary>
/// Cycle de vie d'une course : chaque valeur représente un état stable par lequel la course transite
/// depuis la demande initiale du client jusqu'à sa clôture (complétée ou annulée).
/// </summary>
public enum RideStatus { Pending, Offered, Accepted, DriverArrived, InProgress, Completed, Cancelled }
