# KT1 — ACC-04 and ADM-08

**Assigned scope:** ACC-04 (J), concrete-resource authorization; ADM-08 (N), multi-course administration with delegation. The authoritative requirement IDs and minimum acceptance criteria are in `docs/spec/PROJECT_SPECIFICATION_OIB.pdf`, sections 4.2 and 4.12. Legacy Scutum requirement IDs do not identify this assignment.

This is a started, demonstrable implementation with synthetic HTTP sessions and a repeatable in-memory simulator. It is not a production authentication system or a completed implementation of every platform module. The specification requires an ACC-04 student to be unable to read another student's attempt by direct URL; for ADM-08 it requires verifiable separation of administrative and teaching power. Both boundaries are implemented and tested here.

## Five presentation requirements

| Requirement | Concrete evidence | Demonstration / limitation |
|---|---|---|
| Started implementation | `backend/Platform.Api/Modules/Access`, `Administration`, `Kt1`; `frontend/src/components/CourseworkKt1.tsx` | Launch profile `AccessKt1`; two ACC-04 operations and an ADM-08 delegation lifecycle |
| Own contribution | New task-linked branch, the student's actual commit and PR | Run `git log --oneline` and show the PR diff. No invented authors, backdated work, or separate contribution file |
| Architecture of each assigned component | `diagrams/acc04-architecture.mmd/.svg`, `adm08-architecture.mmd/.svg` | Components, responsibilities, public interfaces, shared decision engine and provider boundaries |
| DFD of each assigned component | `diagrams/acc04-dfd.mmd/.svg`, `adm08-dfd.mmd/.svg` | Actors, input/output, processes, stores and client/server/provider trust boundaries |
| Short demonstration | `scripts/kt1-demo.ps1`, React demo, `DEMO.md` | Own draft read/write, IDOR denial, scoped administration, delegation, settings update and immediate revocation |

## All mandatory obligations

| Obligation | Repository evidence | What still requires real external action |
|---|---|---|
| Use cases, order and prerequisites for every assigned requirement in Tapiz | `BACKLOG.md`, `backlog.json`, `scripts/prepare-access-task.ps1` | Create actual cards, record their IDs/URLs, assign actual members and dates. Prepared descriptions do not create cards |
| Unified model, states, ownership and Subject–Action–Resource matrix | `DOMAIN_MODEL.md`, domain and flow diagrams | Review with the actual neighboring providers |
| Structure, startup README, successful build, initial CI, task → branch → PR | Root README; `.github/workflows/ci.yml`; `VALIDATION.md` | Push this new branch, open a new PR, inspect its actual CI. Never reuse old PR/test counts as new evidence |
| Complete primary flow through application logic and simulator; two operations | `AttemptService.Execute`: read and update own draft; snapshot-bound storage and CAS | Persistent ATT adapter is KT2 work |
| Server authorization, validation, automated negative test | Shared `IAccessDecision` + `AttemptPolicy` / `AdministrationPolicy`; `Platform.Kt1.Tests` | Production identity/membership integration |
| Threat model for both assigned requirements | `THREAT_MODEL.md`: asset → misuse → control → named test → evidence | Deployment and provider-specific threats must be revisited at integration |
| Repeatable state and structured secret-free change audit | `DemoStore`; deterministic IDs; restart-to-reset; `SecurityEvent` / `KT1_AUDIT_JSON` | Durable database migration and transactional audit adapter in KT2 |
| At least one reviewed neighboring-team contract; test double with positive and negative replies | `CONTRACTS.md`, current shared access interfaces, found/missing/unavailable provider tests | Actual neighboring-team acknowledgement is **PENDING**; attach its real review reference |

The code/documentation portion is prepared. Full administrative closure of KT1 also needs the real Tapiz trace, actual PR/CI evidence and at least one actual contract review. A local ZIP cannot certify those actions.

## Relationship to shared platform code

The existing `backend/Platform.Access` project and its shared team's documentation are preserved unchanged. This contribution registers six concrete actions and two policies through the existing catalog/registry and calls the published `IAccessDecision.DecideAsync` contract. It does not introduce another decision engine. The original shared `/api/access/demo-deny` route remains available in Development.

The student's former contextual/time-window feature is absent from the new owned code, fixtures, tests and diagrams. ADM-08 uses explicit course administrative grants, not teaching roles or a generic global-admin bypass. Shared specification catalogs and other teams' existing components are not rewritten.

## Remaining work through KT2 (exercise 8)

1. Record real Tapiz cards and review the ATT snapshot/CAS and identity boundaries with the assigned neighboring teams.
2. Integrate actual authenticated account/membership and ATT providers, with current shared decision contracts maintained.
3. Replace the simulator with course/grant persistence, migrations, optimistic concurrency and durable audit in the same transaction.
4. Agree provisioning/recovery of root course administrators; the KT1 seed is not a root-role management API.
5. Integrate the real administrative UI and the agreed course metadata contract. Teaching assignment, grades, account provisioning and course creation remain with their owners.
6. Re-run cross-module integration and deployment tests and attach real CI evidence.

## Detailed-review corrections

The previously weak existing-session proof is corrected: the HTTP lifecycle uses separate clients, and two dedicated read/write regression cases explicitly reuse the signed cookie captured before revocation with automatic cookie handling disabled. The live demo also preserves its separate delegate client and checks one sign-in and the same cookie after revocation. Both component DFDs now show provider/store responses and denial paths, including metadata-only auditing. External closure instructions and a reference-recording helper are in `CLOSURE.md`; those cannot substitute for actual remote cards, approvals, contributions or successful CI.
