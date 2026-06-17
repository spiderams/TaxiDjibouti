using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Taxi.Application.Realtime.RideAccess;
using Taxi.Application.Realtime.UpdateDriverLocation;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Realtime;

/// <summary>
/// Hub SignalR du suivi de course : clients et chauffeurs rejoignent des groupes (course, chauffeur, admin) et reçoivent position et changements de statut en temps réel. Le chauffeur diffuse sa position via <c>SendDriverLocation</c>.
/// </summary>
[Authorize]
public sealed class RideHub(
    ICommandHandler<UpdateDriverLocationCommand, DriverLocationBroadcast> locationHandler,
    IQueryHandler<RideAccessQuery, bool> accessHandler) : Hub
{
    /// <summary>
    /// Ajoute la connexion courante au groupe "Drivers" pour recevoir les notifications destinées aux chauffeurs.
    /// </summary>
    public Task JoinDriversGroup()
        => Groups.AddToGroupAsync(Context.ConnectionId, "Drivers");

    /// <summary>
    /// Ajoute la connexion au groupe personnel du chauffeur authentifié ("DriverUser_{userId}") pour recevoir les offres de course qui lui sont adressées.
    /// </summary>
    public Task JoinMyDriverGroup()
    {
        var userId = Context.User!.GetUserId();
        if (userId is null)
            return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, $"DriverUser_{userId}");
    }

    /// <summary>
    /// Ajoute la connexion au groupe "Admins" ; ignoré si l'utilisateur n'est pas dans le rôle Admin.
    /// </summary>
    public Task JoinAdminsGroup()
    {
        if (!Context.User!.IsInRole(RoleNames.Admin))
            return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
    }

    /// <summary>
    /// Ajoute la connexion au groupe du client donné ("Client_{clientId}") pour recevoir les mises à jour de sa course ; autorisé uniquement si l'utilisateur est admin ou est le client lui-même.
    /// </summary>
    public Task JoinClientGroup(string clientId)
    {
        var userId = Context.User!.GetUserId();
        if (!Context.User.IsInRole(RoleNames.Admin) && clientId != userId)
            return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, $"Client_{clientId}");
    }

    /// <summary>
    /// Ajoute la connexion au groupe de la course ("Ride_{rideId}") après vérification d'accès ; seuls le client, le chauffeur concerné et les admins sont autorisés.
    /// </summary>
    public async Task JoinRideGroup(int rideId)
    {
        var userId = Context.User!.GetUserId();
        if (userId is null)
            return;

        var isAdmin = Context.User.IsInRole(RoleNames.Admin);
        var access = await accessHandler.Handle(new RideAccessQuery(rideId, userId, isAdmin), Context.ConnectionAborted);
        if (access.IsSuccess && access.Value)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Ride_{rideId}");
    }

    /// <summary>
    /// Reçoit la position GPS du chauffeur, la persiste via le handler, puis diffuse la mise à jour aux groupes client, course et admins.
    /// </summary>
    public async Task SendDriverLocation(DriverLocationDto location)
    {
        var userId = Context.User!.GetUserId();
        if (userId is null)
            throw new HubException("Utilisateur non authentifié.");

        var command = new UpdateDriverLocationCommand(
            userId, location.RideId, location.Latitude, location.Longitude, location.Heading, location.Speed);

        var result = await locationHandler.Handle(command, Context.ConnectionAborted);
        if (!result.IsSuccess)
            throw new HubException(result.Error.Description);

        var payload = result.Value;
        await Clients.Group($"Client_{payload.ClientId}").SendAsync("driverLocationUpdated", payload);
        await Clients.Group($"Ride_{payload.RideId}").SendAsync("driverLocationUpdated", payload);
        await Clients.Group("Admins").SendAsync("driverLocationUpdated", payload);
    }
}
