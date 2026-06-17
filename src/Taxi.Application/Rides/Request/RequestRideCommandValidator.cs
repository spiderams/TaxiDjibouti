using FluentValidation;

namespace Taxi.Application.Rides.Request;

/// <summary>
/// Règles de validation de <see cref="RequestRideCommand"/> : les adresses et zones tarifaires sont obligatoires.
/// </summary>
internal sealed class RequestRideCommandValidator : AbstractValidator<RequestRideCommand>
{
    public RequestRideCommandValidator()
    {
        RuleFor(c => c.PickupAddress).NotEmpty();
        RuleFor(c => c.DestinationAddress).NotEmpty();
        RuleFor(c => c.PickupZone).NotEmpty();
        RuleFor(c => c.DestinationZone).NotEmpty();
    }
}
