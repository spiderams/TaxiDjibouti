namespace Taxi.SharedKernel;

/// <summary>
/// Représente l'issue d'une opération (succès ou échec) sans lever d'exception pour les erreurs métier attendues. Porte une <see cref="Error"/> en cas d'échec.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Un succès ne peut pas porter d'erreur.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Un échec doit porter une erreur.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// Résultat d'une opération portant une valeur en cas de succès. Conversion implicite depuis <c>T</c>.
/// </summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error)
        => _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("On ne peut pas lire la valeur d'un résultat en échec.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
