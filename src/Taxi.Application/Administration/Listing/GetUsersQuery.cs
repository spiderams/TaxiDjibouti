using Taxi.Application.Administration;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

/// <summary>
/// Requête administrative retournant la liste de tous les comptes utilisateurs avec leurs rôles.
/// </summary>
public sealed record GetUsersQuery : IQuery<IReadOnlyList<UserSummary>>;
