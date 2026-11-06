using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Api.Services;

/// <summary>
/// Reads the correlation id from <c>X-Correlation-Id</c>. A missing or invalid header falls back
/// to the request's TraceIdentifier.
/// </summary>
public sealed partial class HttpCorrelationContext : ICorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    public HttpCorrelationContext(IHttpContextAccessor accessor)
    {
        var context = accessor.HttpContext;
        if (context is null)
        {
            CorrelationId = Guid.NewGuid().ToString();
            return;
        }

        // Only short plain values, the id ends up in audit records and logs
        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues values)
            && values.Count == 1
            && values[0] is { } value
            && SafeId().IsMatch(value))
        {
            CorrelationId = value;
            return;
        }

        CorrelationId = context.TraceIdentifier;
    }

    public string CorrelationId { get; }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeId();
}
