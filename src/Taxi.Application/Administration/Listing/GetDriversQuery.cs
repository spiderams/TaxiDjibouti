using Taxi.Application.Drivers;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Requête administrative retournant la liste complète des profils chauffeurs enregistrés.
/// </summary>
public sealed record GetDriversQuery : IQuery<IReadOnlyList<DriverDto>>;
