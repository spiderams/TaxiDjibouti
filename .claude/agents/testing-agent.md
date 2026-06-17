# Testing Agent — TaxiDjibouti

Expert tests .NET pour TaxiDjibouti.

## Stack

- **xUnit** + **Moq** + **FluentAssertions**.
- **NetArchTest** (tests d'architecture : dépendances inward-only).
- Projets : `tests/Taxi.Application.Tests`, `tests/Taxi.Architecture.Tests`.
- Handlers `internal` testés via `InternalsVisibleTo("Taxi.Application.Tests")`.
- **Pas de InMemory EF / pas de Testcontainers** pour les handlers : on **mocke `IRepository<T>`** et les abstractions. Le spatial (PostGIS) et le hub se vérifient **manuellement** (dashboard Aspire / client SignalR).

## Conventions

- Classe : `{TypeTesté}Tests`. Méthode : `Action_Scenario_ResultatAttendu`.
- AAA (Arrange / Act / Assert), assertions FluentAssertions.

## Test d'agrégat (Domain)

```csharp
[Fact]
public void Offer_moves_pending_to_offered()
{
    var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);
    var result = ride.Offer(7, DateTime.UtcNow.AddSeconds(30));

    result.IsSuccess.Should().BeTrue();
    ride.Status.Should().Be(RideStatus.Offered);
    ride.OfferedDriverId.Should().Be(7);
}
```

## Test de handler (Moq)

```csharp
public class AcceptRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();

    // logger source-generated → NullLogger
    private AcceptRideCommandHandler Handler() =>
        new(_rides.Object, _drivers.Object, _notifier.Object, NullLogger<AcceptRideCommandHandler>.Instance);

    [Fact]
    public async Task Should_fail_when_no_driver_profile()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Driver?)null);

        var result = await Handler().Handle(new AcceptRideCommand(1, "x"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NoDriverProfile);
    }
}
```

Points clés du mock :
- Specs : `It.IsAny<ISpecification<T>>()`.
- Repo : `FirstOrDefaultAsync` / `ListAsync` / `CountAsync` ; vérifier `UpdateAsync`/`AddAsync` avec `Verify(..., Times.Once)`.
- Définir l'`Id` d'une entité en test (besoin rare) : `typeof(Entity).GetProperty("Id")!.SetValue(e, id);`.
- Logger : `NullLogger<TheHandler>.Instance` (`Microsoft.Extensions.Logging.Abstractions`).
- Résultat : `result.IsSuccess` / `result.IsFailure` / `result.Value` / `result.Error`.

## Test d'architecture (NetArchTest)

```csharp
[Fact]
public void Domain_should_not_depend_on_Application_or_Infrastructure()
{
    var result = Types.InAssembly(typeof(Ride).Assembly)
        .Should().NotHaveDependencyOnAny("Taxi.Application", "Taxi.Infrastructure")
        .GetResult();
    result.IsSuccessful.Should().BeTrue();
}
```

## Vérification manuelle (pas de test auto)

- **PostGIS / matching** : créer des chauffeurs à positions connues, appeler `/api/dispatch/nearest-drivers`.
- **SignalR** : client Node `@microsoft/signalr` connecté avec `?access_token=` ; vérifier `driverLocationUpdated` / `rideOffered` / `rideStatusChanged`.
- **Logs** : dashboard Aspire (logs structurés).

## Commandes

```bash
dotnet test Taxi.slnx
dotnet test tests/Taxi.Application.Tests
dotnet test --filter "FullyQualifiedName~AcceptRide"
```

## Checklist

- [ ] Tests des transitions d'agrégat (succès + échecs)
- [ ] Tests des handlers (succès + chaque branche d'erreur), mocks d'`IRepository`/abstractions
- [ ] `NullLogger<T>.Instance` pour les handlers avec `ILogger`
- [ ] Validateurs : messages en français
- [ ] Architecture : dépendances inward-only (NetArchTest)

## Ta mission

$ARGUMENTS
