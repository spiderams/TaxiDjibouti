namespace Taxi.SharedKernel;

/// <summary>
/// Catégorie d'erreur, mappée vers un statut HTTP (Validation→400, NotFound→404, Conflict→409, Unauthorized→401, Forbidden→403, Failure→500).
/// </summary>
public enum ErrorType { None = 0, Failure = 1, Validation = 2, NotFound = 3, Conflict = 4, Unauthorized = 5, Forbidden = 6 }

/// <summary>
/// Erreur métier typée : code + description + <see cref="ErrorType"/>. Convertie en code HTTP par la couche Web.
/// </summary>
public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}
