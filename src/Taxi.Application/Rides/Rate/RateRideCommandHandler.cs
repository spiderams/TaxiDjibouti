using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Rate;

/// <summary>
/// Gère <see cref="RateRideCommand"/> : crée ou met à jour la note de la course et recalcule
/// la moyenne du chauffeur après chaque évaluation.
/// </summary>
internal sealed partial class RateRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Rating> ratings,
    IRepository<Driver> drivers,
    ILogger<RateRideCommandHandler> logger)
    : ICommandHandler<RateRideCommand, RatingDto>
{
    public async Task<Result<RatingDto>> Handle(RateRideCommand command, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RatingDto>(RideErrors.NotFound);
        if (ride.ClientId != command.ClientId)
            return Result.Failure<RatingDto>(RideErrors.NotAssignedDriver);
        if (ride.Status != RideStatus.Completed)
            return Result.Failure<RatingDto>(RatingErrors.RideNotCompleted);
        if (ride.DriverId is null)
            return Result.Failure<RatingDto>(RatingErrors.NoDriver);

        var driverId = ride.DriverId.Value;

        var existing = await ratings.FirstOrDefaultAsync(new RatingByRideSpec(command.RideId), cancellationToken);
        Rating rating;
        if (existing is null)
        {
            rating = Rating.Create(ride.Id, command.ClientId, driverId, command.Score, command.Comment);
            await ratings.AddAsync(rating, cancellationToken);
        }
        else
        {
            existing.UpdateScore(command.Score, command.Comment);
            await ratings.UpdateAsync(existing, cancellationToken);
            rating = existing;
        }

        var driverRatings = await ratings.ListAsync(new RatingsByDriverSpec(driverId), cancellationToken);
        var average = driverRatings.Average(r => r.Score);

        var driver = await drivers.FirstOrDefaultAsync(new DriverByIdSpec(driverId), cancellationToken);
        if (driver is not null)
        {
            driver.UpdateAverageRating(average);
            await drivers.UpdateAsync(driver, cancellationToken);
        }

        LogRideRated(logger, ride.Id, driverId, average);
        return RatingDto.From(rating);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} notée ; moyenne du chauffeur {DriverId} recalculée à {Average:0.0}")]
    private static partial void LogRideRated(ILogger logger, int rideId, int driverId, double average);
}
