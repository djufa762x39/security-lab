# S-12 Identity demo producers

S-12 connects the existing Identity login, logout and account-status flows to the
shared AUD-01 contract. The mapping is centralized in `IdentityAuditProducer`; the
Identity services do not construct audit records or copy request text into details.

## Implemented mappings

| Identity result | Audit event | Actor | Details |
|---|---|---|---|
| Successful login | `auth.login` / `Success` | confirmed user | none |
| Invalid credentials | `auth.login` / `Failure` | anonymous | stable `reasonCode` |
| Confirmed inactive account | `auth.login` / `Failure` | confirmed user | stable `reasonCode` |
| Successful logout | `auth.logout` / `Success` | user captured before revocation | none |
| Account status changed | `account.status.changed` / `Success` | administrator | `oldStatus`, `newStatus` |
| Rejected status change | `account.status.changed` / `Denied` or `Failure` | trusted caller, or anonymous when unauthenticated | stable `reasonCode` |

Passwords, typed e-mail addresses, tokens, session identifiers and the administrator's
free-text reason are never accepted by the producer API. Server correlation IDs are
forwarded through `AuditContext`; occurrence time remains the responsibility of
`AuditWriter` and its injected `TimeProvider`.

The persistent writer is registered through `AddAudit(IConfiguration)`. Its runtime
connection string is `ConnectionStrings:Audit`; the API never runs Audit migrations.
Missing or unavailable storage produces `Unavailable`, not a successful audit claim.

## Failure behaviour

- A successful login is not returned to the client unless its audit write is recorded.
- Logout remains revoked when its audit write is unavailable and raises a sanitized
  technical alarm.
- The demo account-status repository restores the old status when the audit write is
  not recorded, and session revocation happens only after a recorded event.
- Once an action has taken effect, its audit attempt uses `CancellationToken.None`, so
  disconnecting the HTTP client cannot cancel the evidence write.

## S-16 handoff

The current account-status repository is in memory. Its compensation test demonstrates
the S-12 fail-closed decision, but it is not evidence of database atomicity. S-16 must:

1. persist the rights/status change and Audit insert in one PostgreSQL transaction (or
   use an agreed transactional outbox);
2. force an Audit failure and verify from a new database session that the rights change
   was not committed;
3. use the restricted Audit runtime role and keep migrations outside the API process.

S-16 should reuse `IdentityAuditProducer`, the shared catalog and `ITrustedAuditWriter`;
it must not reintroduce Identity's legacy free-text `IAuditWriter` mapping.
