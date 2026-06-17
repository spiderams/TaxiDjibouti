using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.GetMe;

/// <summary>
/// Requête pour récupérer le profil de l'utilisateur actuellement connecté à partir de son identifiant.
/// </summary>
public sealed record GetMeQuery(string UserId) : IQuery<UserInfo>;
