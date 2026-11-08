# S-13: Access forwarder (AUD-01, AUD-04)

Owner: member 9 (Aleksa Mandarić). Review: member 10; Access composition also team 3; `Program.cs` also member 1.

Status: **implemented, not wired**. The forwarder, its mapping, loss counters and an explicit composition method
exist. They are covered by unit tests and by host tests that run the real `Platform.Api` host and the real Access
middleware against PostgreSQL. `Program.cs` does **not** call `AddAccessAuditForwarding()` yet, so the running API
stores no Access decision in Audit today. Integration is not claimed until decision D2 (and D6, see below) is
accepted by team 3 and member 1.

Checked against `main` 340244c (08.11.2026): PR #109 (ACC-05) enabled `ValidateScopes`/`ValidateOnBuild` in
`Platform.Api`; it added no public sink hook and did not change the middleware correlation id. PR #57 (S-08) added
the `Platform.Audit` → `Platform.Access` project reference that the forwarder also needs.

## What it does

`AccessDecisionAuditForwarder` decorates the `IAccessAuditWriter` that `AddAccessControl()` registers:

1. The decision goes to the existing Access chain unchanged (filtering, log sink, resilience stay as they are).
2. A **Deny** decision is mapped to an `access.denied` draft and written through `ITrustedAuditWriter`.
3. **Allow** decisions are not written to Audit: the catalog has no event type for them (decision D6).

| Access value | Audit value |
| --- | --- |
| `Subject == null` | `AuditActor.Anonymous` |
| `Subject.Id` is a non-empty GUID | `AuditActor.ForUser(id)` |
| `Subject.Id` is anything else | not written; alarm `invalid_subject` (no actor is invented) |
| `AccessRequest.CorrelationId` | `AuditContext.CorrelationId`; an invalid value is not replaced, alarm `invalid_correlation_id` |
| `Resource` of type `course` with a GUID id | `CourseId` and `Resource = Course/{id}` |
| other resources | no course (a trusted resource resolver is needed first) |
| `action`, `denialReason`, `policyId`, `policyVersion` | detail keys of the same name, only when the value matches the catalog format |

## Lifetimes (D2, open)

`IAccessDecision` and `IAccessAuditWriter` are singletons; `ITrustedAuditWriter` is scoped (`AuditDbContext`).
The forwarder stays a singleton and opens its own scope per write through `IServiceScopeFactory`, so no scoped
service is captured. Plan v2.2 proposes request-scoped decision and writer instead; that needs a change to the
Access composition and team 3 review.

## Registration

`services.AddAccessAuditForwarding()` must be called once, after `AddAccessControl()` and `AddAuditPersistence(...)`.
It fails at startup when called twice, when either module is missing, or when a later registration replaces the
forwarder (`AccessAuditForwardingStartupCheck`).

Preferred long-term option (A): team 3 exposes a public hook for a sink inside its chain. Current option (B):
decorator around the registered writer, without changing Access code.

Wiring the decorator in `Program.cs` is one line after `AddAuditPersistence(...)`, but it is held back on purpose:

- **D2**: team 3 has not confirmed option B; their `ACC-05-ACC-08.md` still lists the `Platform.Audit` integration as
  open step 4.
- **D6**: the decorator wraps the outermost Access writer, so it stores **every** Deny. Team 3's
  `AggregatingAccessAuditSink` exists to limit repeated denials in their log; the forwarder does not use it and does
  not sample. The expected Deny volume and database load must be agreed before wiring.

## Failure behaviour

Rejected, unavailable, failing or canceled audit writes never throw. The forwarder logs
`audit.access_forwarding.failure` (event id 6201) with a fixed reason, an enum or exception type name and the
correlation id only when it matches the Audit format. The access decision itself is unchanged: Deny stays 401/403.

Access waits for its audit writer only up to `AccessDecisionOptions.AuditTimeout` (1 s). A slower database write is
not cancelled; it finishes after the response, so the decision is never delayed by more than that timeout.

## Loss counters (D6, AUD-10)

Meter `Platform.Audit.AccessForwarding` (through the host `IMeterFactory`):

| Instrument | Meaning |
| --- | --- |
| `audit.access_forwarding.recorded` | `access.denied` records stored |
| `audit.access_forwarding.failures` | Deny decisions not stored, tag `reason` = `rejected`, `unavailable`, `failed`, `canceled` or `unmapped` |

A failure of the existing Access log chain (`access_chain_failed`) is alarmed but not counted as a lost record,
because the Audit write still follows. Tags never carry correlation ids, actors or details.

## Known gap: correlation id (T09)

`AccessControlMiddleware` (line 49 on 340244c) still generates its own id (`Guid.NewGuid().ToString("N")`) instead of
using `ICorrelationContext` from S-10. The chain decision → stored row is proven: the `correlationId` in the 401/403
problem details equals the stored row (`AccessForwardingPostgresTests`). The link HTTP `X-Correlation-Id` → decision
needs that middleware line changed by team 3; until then acceptance criterion 1 is only partly met.

## Tests

`backend/Platform.Audit.Tests/AccessForwarding/`:

- `AccessDecisionAuditMapperTests`: actor, course, detail allowlist, denial reasons, invalid values, real validator accepts the draft.
- `AccessDecisionAuditForwarderTests` (T10): one Deny → one event, Allow → none, failures and cancellation do not
  throw, Access chain failure does not stop Audit, one scope per write, parallel identities, no second call to
  `IAccessDecision`; loss counters per reason; alarm without secrets, identities, details or hostile correlation values.
- `AccessAuditForwardingCompositionTests` (T10 startup): `ValidateScopes`/`ValidateOnBuild`, explicit replacement, order and duplicate checks.
- `AccessForwardingPostgresTests` (T09/T10, needs PostgreSQL via `scripts/audit/test-postgres.ps1`): an anonymous
  request to a test route protected by `audit.events.read` on `Course/{id}` gets 401 and produces exactly one
  `access.denied` row with the same correlation id, anonymous actor and the course from the Access resource; two
  parallel denials do not mix course or correlation.
- `AccessForwardingHostFailureTests` (T10, no database): the real host starts with the forwarder active; with the
  Audit database down the request still gets 401 and the alarm carries no connection details.

The test route exists only in the test assembly (`AccessForwardingHostFactory`); `Platform.Api` never maps it.

Still to do, outside this branch: middleware correlation (team 3), wiring decision D2/D6 (team 3, member 1),
the full HTTP T09 test with `X-Correlation-Id` together with member 10, DFD input for member 7 (S-18).
