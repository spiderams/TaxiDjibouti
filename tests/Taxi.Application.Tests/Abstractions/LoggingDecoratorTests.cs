using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taxi.Application.Abstractions.Behaviors;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;
using Xunit;

namespace Taxi.Application.Tests.Abstractions;

public class LoggingDecoratorTests
{
    private sealed record DummyQuery : IQuery<int>;

    private sealed class OkHandler : IQueryHandler<DummyQuery, int>
    {
        public Task<Result<int>> Handle(DummyQuery query, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(42));
    }

    private sealed class ThrowHandler : IQueryHandler<DummyQuery, int>
    {
        public Task<Result<int>> Handle(DummyQuery query, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task QueryDecorator_passes_through_success()
    {
        var decorator = new LoggingDecorator.QueryHandler<DummyQuery, int>(
            new OkHandler(), NullLogger<LoggingDecorator.QueryHandler<DummyQuery, int>>.Instance);

        var result = await decorator.Handle(new DummyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task QueryDecorator_rethrows_on_exception()
    {
        var decorator = new LoggingDecorator.QueryHandler<DummyQuery, int>(
            new ThrowHandler(), NullLogger<LoggingDecorator.QueryHandler<DummyQuery, int>>.Instance);

        var act = () => decorator.Handle(new DummyQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
