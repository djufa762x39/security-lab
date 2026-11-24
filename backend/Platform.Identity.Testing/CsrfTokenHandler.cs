using Platform.Api.Services.Identity;

namespace Platform.Identity.Testing;

/// <summary>
/// US-15: makes a test client behave like our frontend. Before the first POST, PUT, PATCH or DELETE it fetches one
/// CSRF pair from <c>GET /api/auth/csrf</c> and then adds the cookie and the <c>X-CSRF-TOKEN</c> header to every
/// state-changing request. A request that already has the header is left alone, so a test can still send a wrong one.
/// Tests that simulate a CSRF attack must use a client without this handler.
/// </summary>
public sealed class CsrfTokenHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CsrfPair? _pair;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsSafe(request.Method) && !request.Headers.Contains(CsrfToken.HeaderName))
        {
            var pair = await GetPairAsync(request.RequestUri!, cancellationToken);
            pair.AppendCookie(request);
            request.Headers.Add(CsrfToken.HeaderName, pair.Token);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsSafe(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options || method == HttpMethod.Trace;

    private async Task<CsrfPair> GetPairAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        if (_pair is not null)
            return _pair;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pair is not null)
                return _pair;

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri, CsrfPair.TokenUrl));
            using var response = await base.SendAsync(tokenRequest, cancellationToken);
            return _pair = await CsrfPair.ReadAsync(response, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _gate.Dispose();
        base.Dispose(disposing);
    }
}
