namespace OmnisRouter.Api;

/// <summary>
/// A known, mapped-status error condition. Caught by <see cref="Middleware.ErrorMappingMiddleware"/>
/// and rendered in the requesting client's wire-format error shape. Anything that escapes as a
/// different exception type is treated as unexpected and mapped to a generic 500.
/// </summary>
public sealed class OmnisException : Exception
{
    /// <summary>HTTP status code to respond with.</summary>
    public int StatusCode { get; }

    /// <summary>Stable, machine-readable error code (surfaced in the mapped error body).</summary>
    public string Code { get; }

    public OmnisException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
