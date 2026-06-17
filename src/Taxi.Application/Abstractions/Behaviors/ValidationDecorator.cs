using FluentValidation;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Abstractions.Behaviors;

/// <summary>
/// Décorateurs qui valident la commande/requête (FluentValidation) AVANT le handler ; renvoient une erreur de validation sans exécuter le handler si la requête est invalide.
/// </summary>
public static class ValidationDecorator
{
    public sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var failure = await Validate(command, validators, cancellationToken);
            return failure is null
                ? await inner.Handle(command, cancellationToken)
                : Result.Failure<TResponse>(failure);
        }
    }

    public sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        IEnumerable<IValidator<TQuery>> validators)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var failure = await Validate(query, validators, cancellationToken);
            return failure is null
                ? await inner.Handle(query, cancellationToken)
                : Result.Failure<TResponse>(failure);
        }
    }

    private static async Task<Error?> Validate<T>(
        T request, IEnumerable<IValidator<T>> validators, CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(new ValidationContext<T>(request), cancellationToken);
            var firstError = result.Errors.FirstOrDefault();
            if (firstError is not null)
            {
                var code = string.IsNullOrEmpty(firstError.PropertyName) ? "Validation" : firstError.PropertyName;
                return Error.Validation(code, firstError.ErrorMessage);
            }
        }

        return null;
    }
}
