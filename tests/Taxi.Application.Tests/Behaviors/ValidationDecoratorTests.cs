using FluentAssertions;
using FluentValidation;
using Taxi.Application.Abstractions.Behaviors;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;
using Xunit;

namespace Taxi.Application.Tests.Behaviors;

public class ValidationDecoratorTests
{
    public sealed record DummyCommand(int Amount) : ICommand<int>;

    public sealed class DummyValidator : AbstractValidator<DummyCommand>
    {
        public DummyValidator() => RuleFor(c => c.Amount).GreaterThan(0);
    }

    private sealed class PassThroughHandler : ICommandHandler<DummyCommand, int>
    {
        public Task<Result<int>> Handle(DummyCommand command, CancellationToken ct)
            => Task.FromResult(Result.Success(command.Amount));
    }

    [Fact]
    public async Task Should_return_validation_failure_when_invalid()
    {
        var decorator = new ValidationDecorator.CommandHandler<DummyCommand, int>(
            new PassThroughHandler(), new IValidator<DummyCommand>[] { new DummyValidator() });

        var result = await decorator.Handle(new DummyCommand(0), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Should_call_inner_handler_when_valid()
    {
        var decorator = new ValidationDecorator.CommandHandler<DummyCommand, int>(
            new PassThroughHandler(), new IValidator<DummyCommand>[] { new DummyValidator() });

        var result = await decorator.Handle(new DummyCommand(5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(5);
    }
}
