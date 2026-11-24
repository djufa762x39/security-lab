using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Identity.Testing;

/// <summary>US-15: a CSRF token pair as a browser holds it after <c>GET /api/auth/csrf</c>.</summary>
/// <param name="Cookie">The <c>name=value</c> part of the HttpOnly cookie, ready to be joined with other cookies.</param>
/// <param name="Token">The request token for the <c>X-CSRF-TOKEN</c> header.</param>
public sealed record CsrfPair(string Cookie, string Token)
{
    public const string TokenUrl = "/api/auth/csrf";

    /// <summary>Asks the API for a token pair, as the frontend does when it starts.</summary>
    public static async Task<CsrfPair> FetchAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(TokenUrl, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Reads the pair from a <c>GET /api/auth/csrf</c> response.</summary>
    public static async Task<CsrfPair> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        response.EnsureSuccessStatusCode();

        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CsrfToken.CookieName + "=", StringComparison.Ordinal))
            .Split(';')[0];
        var body = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(cancellationToken);

        return new CsrfPair(cookie, body!.Token);
    }

    /// <summary>Adds the cookie (next to an optional session cookie) and the header to one request.</summary>
    public void ApplyTo(HttpRequestMessage request, string? sessionToken = null)
    {
        if (sessionToken is not null)
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={sessionToken}");

        AppendCookie(request);
        request.Headers.Add(CsrfToken.HeaderName, Token);
    }

    /// <summary>The same for a request built directly on <c>TestServer.SendAsync</c>.</summary>
    public void ApplyTo(HttpRequest request)
    {
        var existing = request.Headers.Cookie.ToString();
        request.Headers.Cookie = string.IsNullOrEmpty(existing) ? Cookie : $"{existing}; {Cookie}";
        request.Headers[CsrfToken.HeaderName] = Token;
    }

    // One Cookie header, as a browser sends it: the CSRF cookie is joined to cookies the test already set.
    internal void AppendCookie(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues("Cookie", out var existing))
        {
            var joined = string.Join("; ", existing.Append(Cookie));
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", joined);
            return;
        }

        request.Headers.Add("Cookie", Cookie);
    }
}
