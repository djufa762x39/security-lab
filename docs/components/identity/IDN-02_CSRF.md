# IDN-02 CSRF protection (US-15)

**Team:** 1 (IDN-02). **Specification requirements:** SEC-03 (primary), IDN-02, SEC-01, AUD-03, OPS-02. Depends on US-04 (login cookie). Data flow: process P7, flows F1, F4, F21 and F22 in [`IDN-02_IDN-06_DFD.md`](./IDN-02_IDN-06_DFD.md).

## The rule

A request that changes state (any method except `GET`, `HEAD`, `OPTIONS`, `TRACE`) must carry a valid CSRF token when:

- it carries the session cookie `SecurityLab.Session`, on any route of any module, or
- it is the login (`POST /api/auth/login`), which has no session yet (login CSRF).

Without a valid token the request is refused before the endpoint runs and nothing changes:

| Result | Status | Body |
|-|-|-|
| Token missing, empty, forged, from another browser, or its cookie is missing | `403` | `{ "code": "csrf_invalid" }`, `Cache-Control: no-store` |

Deliberately not checked:

| Request | Why |
|-|-|
| `GET`, `HEAD`, `OPTIONS`, `TRACE` | They must not change state, so there is nothing to forge. |
| State-changing request without the session cookie | No ambient credential to abuse. Development callers that use a header (`X-Test-User`) cannot be forged, because a foreign page cannot add headers. |
| `POST /api/auth/logout` | The one documented exception. A stale or missing token must never keep a session alive (IDN-02), and a forged logout only signs the user out. |
| OIDC callback `/api/auth/oidc/callback` | Finished by the OIDC handler before the check runs. It is protected by `state`, `nonce` and PKCE. |

`SameSite=Strict` on the session cookie stays as a second layer; it is not a replacement, because a sibling subdomain counts as the same site.

## Getting a token

`GET /api/auth/csrf` (public, `identity.csrf.token`):

| Cookie / body | Flags | Content |
|-|-|-|
| `__Host-SecurityLab.Csrf` | `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/` | Antiforgery cookie token (the secret half). Issued only when the browser has no valid one, so other tabs keep working. |
| `__Host-SecurityLab.CsrfToken` | `Secure`, `SameSite=Strict`, `Path=/`, readable by JavaScript | The request token. |
| body `{ "token": "..." }` | `Cache-Control: no-store` | The same request token. |

The `__Host-` prefix makes the browser accept the cookies only with `Secure`, `Path=/` and no `Domain`, so another subdomain cannot plant its own pair.

The client sends the request token back in the header `X-CSRF-TOKEN`. The server checks it against the cookie token with ASP.NET Core antiforgery (Data Protection), so there is no custom cryptography (specification 3.3). A foreign page cannot read either cookie or the response, so it cannot build the header.

## Code

| Part | File |
|-|-|
| Names and cookie options | `backend/Platform.Api/Services/Identity/CsrfToken.cs` |
| Check | `backend/Platform.Api/Middleware/CsrfProtectionMiddleware.cs`, registered in `Program.cs` after `UseAuthentication` |
| Token endpoint | `AuthController.Csrf` (`GET /api/auth/csrf`), response `CsrfTokenResponse` |
| Access action and policy (ACC-05) | `IdentityAccessActions.CsrfToken`, `CsrfTokenPolicy` |
| Frontend | `frontend/src/auth/csrf.ts` (`csrfFetch`), used by `apiFetch`, `apiRequest` and the direct calls listed below. `AuthProvider` fetches the cookies when the app starts. |
| Test support | `backend/Platform.Identity.Testing` (`CsrfTokenHandler`, `CreateCsrfClient`, `CsrfPair`) |

The middleware runs after authentication, so it sees the same `HttpContext.User` as the endpoint that issued the token. It never logs the token or the cookie (AUD-03).

### Frontend

`csrfFetch(input, init, send?)` is a drop-in replacement for `fetch`. For a state-changing method it copies the readable cookie into `X-CSRF-TOKEN` synchronously, so requests leave at once and in the same order. On `403 csrf_invalid` it calls `GET /api/auth/csrf` and repeats the request exactly once. Any other `403` is returned unchanged.

Calls that change state and use `csrfFetch`: `auth/apiClient.ts` (login, logout, `request`), `api/http.ts` (`apiRequest`), `api/accounts.ts`, `api/labs.ts` (`createLab`), `services/courseService.ts`, `components/labs/EditTaskForm.tsx`, `components/CourseworkKt1.tsx`. A new call that changes state must use `csrfFetch` or one of these clients.

### Tests for other modules

A test client that calls the API with a session cookie and changes state needs a token, as the frontend does:

```csharp
using Platform.Identity.Testing;

using var client = factory.CreateCsrfClient();   // instead of factory.CreateClient()
```

For a request built on `TestServer.SendAsync`, fetch a `CsrfPair` and call `pair.ApplyTo(context.Request)`. Tests that simulate an attack use a plain client.

## Tests

`backend/Platform.Identity.Tests/Integration/CsrfProtectionIntegrationTests.cs` (categories `US-15`, `SEC-03`), real pipeline and PostgreSQL:

- Attack: revoking a session with the cookie but without a token (`Origin: https://evil.example`) returns `403 csrf_invalid` and the session stays valid.
- The same request with a valid token returns `204` and revokes the session.
- A token from another browser, a forged or empty token, and a token without its cookie are refused.
- Login without a token returns `403` and sets no session cookie; with a token it returns `200`.
- Logout without a token still revokes the session; `GET` with the cookie and no token is not checked; a state-changing request without the session cookie gets `401 session_invalid`, not the CSRF answer.
- The token endpoint sets both cookies with the right flags and keeps an existing cookie token.

Frontend: `frontend/src/__tests__/csrf.test.ts`.

```powershell
dotnet test backend\Platform.Identity.Tests --filter "Category=US-15"
cd frontend; npm.cmd test
```

## Limits

- Tokens depend on the Data Protection key ring. Where keys are not kept across restarts (for example a container without a mounted key folder), a restart invalidates existing tokens; the frontend gets one `403 csrf_invalid`, fetches a new token and repeats the request.
- The KT1 demo cookie (`SecurityLab.CourseworkKt1`) is not covered; that demo is Development-only and owned by its team.
