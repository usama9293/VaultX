using Microsoft.AspNetCore.Mvc;

namespace PasswordManager.API.Middleware;

public sealed class RequestBodySizeLimitMiddleware
{
    private const long MaximumEntryBodyBytes = 32 * 1024;
    private readonly RequestDelegate _next;

    public RequestBodySizeLimitMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/vault/entries")
            && context.Request.ContentLength is > MaximumEntryBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Payload Too Large",
                Detail = "The request body exceeds the maximum allowed size."
            });
            return;
        }

        await _next(context);
    }
}
