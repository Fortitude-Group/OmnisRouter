using OmnisRouter.Core.Model;

namespace OmnisRouter.Api.Middleware;

/// <summary>
/// Global error-handling middleware. Catches unhandled exceptions and writes an error body shaped
/// like the requesting client's own wire format, so existing clients can parse the failure without
/// special-casing OmnisRouter (spec: contracts/wire-formats.md "Error contract"). A well-known
/// <see cref="OmnisException"/> maps to its declared status/code; anything else maps to a generic
/// 500 that never leaks internals (no stack traces, no prompt/key content).
/// </summary>
public sealed class ErrorMappingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorMappingMiddleware> _logger;

    public ErrorMappingMiddleware(RequestDelegate next, ILogger<ErrorMappingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OmnisException ex)
        {
            _logger.LogWarning(ex, "Request failed with mapped error {Code}", ex.Code);
            await WriteErrorAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError,
                "internal_error", "An internal error occurred.");
        }
    }

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted)
        {
            // Streaming responses may have already flushed headers/body; nothing more we can do.
            return Task.CompletedTask;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var format = ResolveClientFormat(context.Request.Path);

        object body = format switch
        {
            ClientFormat.Anthropic => new
            {
                type = "error",
                error = new { type = code, message },
            },
            ClientFormat.OpenAI => new
            {
                error = new { message, type = code, param = (string?)null, code },
            },
            ClientFormat.Gemini => new
            {
                error = new { code = statusCode, message, status = code },
            },
            _ => ProblemBody(context, statusCode, code, message),
        };

        // Use CancellationToken.None: we want the error body written even if the originating
        // request's cancellation token has already fired.
        return context.Response.WriteAsJsonAsync(body, CancellationToken.None);
    }

    private static object ProblemBody(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.ContentType = "application/problem+json";
        return new
        {
            type = "about:blank",
            title = code,
            status = statusCode,
            detail = message,
        };
    }

    private static ClientFormat? ResolveClientFormat(PathString path)
    {
        var value = path.Value ?? string.Empty;

        if (value.StartsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
        {
            return ClientFormat.Anthropic;
        }

        if (value.StartsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return ClientFormat.OpenAI;
        }

        if (value.StartsWith("/v1beta/", StringComparison.OrdinalIgnoreCase))
        {
            return ClientFormat.Gemini;
        }

        return null;
    }
}

public static class ErrorMappingMiddlewareExtensions
{
    /// <summary>Registers <see cref="ErrorMappingMiddleware"/> as the outermost error boundary.</summary>
    public static IApplicationBuilder UseOmnisErrorHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ErrorMappingMiddleware>();
}
