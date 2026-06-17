namespace Taxi.Web.Api.Endpoints;

/// <summary>
/// Contrat d'un endpoint Minimal API auto-découvert : chaque endpoint déclare ses routes dans <c>MapEndpoint</c>.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
