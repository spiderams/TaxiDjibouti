using Taxi.SharedKernel;

namespace Taxi.Web.Api.Endpoints;

public static class ResultExtensions
{
    /// <summary>
    /// Convertit un <see cref="Result{T}"/> en réponse HTTP : la valeur en 200, ou le code d'erreur approprié (400/401/403/404/409/500) selon l'<see cref="ErrorType"/>.
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
        => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Problem(statusCode: status, title: error.Code, detail: error.Description);
    }
}
