namespace Taxi.Web.Api.Middleware;

/// <summary>
///     Pose les headers HTTP de sécurité recommandés par OWASP
///     (clickjacking, MIME sniffing, XSS, CSP). Réf : OWASP A05:2021.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        // Exclut la doc/UI : la CSP 'default-src self' bloquerait le CDN Scalar.
        var path = httpContext.Request.Path.Value;
        if (path is not null && (
            path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)))
        {
            await next(httpContext);
            return;
        }

        var headers = httpContext.Response.Headers;
        headers["X-Frame-Options"] = "DENY";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-XSS-Protection"] = "1; mode=block";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        await next(httpContext);
    }
}
