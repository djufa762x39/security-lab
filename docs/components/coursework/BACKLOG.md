# Ordered backlog — ACC-04 and ADM-08

These are prepared tasks derived from specification sections 4.2 and 4.12, not a claim that Tapiz cards already exist. `IMPLEMENTED_LOCAL` concerns code; `EXTERNAL_PENDING` concerns real review/publication. Add actual task IDs, links, assignees and dates in Tapiz. Do not invent historical start/completion dates.

| Order | Task | Phase | Implementation | Prerequisites / use cases | Acceptance evidence |
|---|---|---|---|---|---|
| 1 | ACC04-01 — Read own attempt and reject ID substitution | KT1 | IMPLEMENTED_LOCAL | Trusted account, membership and ATT snapshots; shared decision engine; Read own attempt; Reject foreign and missing IDs | AttemptPolicy.cs; ConcreteReadChecksOwnershipMembershipAndTeachingRole |
| 2 | ACC04-02 — Update own draft with validation and atomic version check | KT1 | IMPLEMENTED_LOCAL | ACC04-01; ATT snapshot/CAS contract; Update draft; Preserve submitted/completed history | AttemptService.cs; ConcurrentDraftWritesWithSameVersionHaveExactlyOneWinner |
| 3 | ACC04-03 — Verify teaching scope and fail closed on provider outage | KT1 | IMPLEMENTED_LOCAL | ACC04-01; positive/negative provider doubles; Teacher reads submitted own-course attempt; Reject blocked account/outage | DependencyOutageDeniesOwnerReadAndWriteWithoutMutation |
| 4 | ADM08-01 — Model two courses with separate administrative and teaching authority | KT1 | IMPLEMENTED_LOCAL | Current account provider; seeded explicit course root grants; shared engine; Read authorized course administration; Separate powers | AdministrationPolicy.cs; AdministrativeGrantDoesNotCreateTeachingMembership |
| 5 | ADM08-02 — Delegate limited course administration and update settings | KT1 | IMPLEMENTED_LOCAL | ADM08-01; whitelist and active target checks; Grant view/settings capability; Delegate changes course display name | AdministrationService.cs; DelegationRenameRevokeIsScopedAndImmediatelyEffective |
| 6 | ADM08-03 — Revoke delegation and reject reused authority | KT1 | IMPLEMENTED_LOCAL | ADM08-02; atomic grant snapshot/version check; Revoke active grant; Recheck existing session | RevocationRejectsPreviouslyCapturedAuthorityAtCommitBoundary |
| 7 | KT1-01 — Prepare domain model, matrix, architecture and DFD for both components | KT1 | PREPARED_LOCAL | Current implementation and selected specification sections; Explain components, data and trust boundaries | DOMAIN_MODEL.md; diagrams/ |
| 8 | KT1-02 — Threat model, repeatable seed and metadata-only change audit | KT1 | IMPLEMENTED_LOCAL | ACC04-02; ADM08-03; Reset synthetic state; Audit authorized changes and denials | THREAT_MODEL.md; DemoStore.cs |
| 9 | KT1-03 — Review at least one provider contract with the actual neighboring team | KT1 | EXTERNAL_PENDING | Prepared CONTRACTS.md and real provider owner; Agree ATT snapshot/CAS or identity boundary | CONTRACTS.md |
| 10 | KT1-04 — Record actual Tapiz task to branch to PR and green CI | KT1 | EXTERNAL_PENDING | Create actual cards; clean main-based branch; commit and push; Publish own contribution; Confirm actual remote checks | EVIDENCE.json; VALIDATION.md |
| 11 | KT1-05 — Demonstrate both flows and automated negative cases | KT1 | IMPLEMENTED_LOCAL | Start AccessKt1 profile; run current code tests; Run HTTP and React demo | scripts/kt1-demo.ps1; DEMO.md |
| 12 | ACC04-04 — Integrate real identity, membership and ATT providers | KT2 | PLANNED | KT1-03 accepted contracts; Use production authenticated subject and ATT storage | CONTRACTS.md |
| 13 | ADM08-04 — Persist grants and course metadata with durable audit | KT2 | PLANNED | ADM08-03; agreed root recovery/course metadata contracts; Provision roots safely; Persist delegation lifecycle | CONTRACTS.md |

## Real Tapiz workflow

1. Use the existing team board. Suggested groups are KT1 — Ownership, KT1 — Course administration, KT1 — Model/evidence, and KT2 — Integration. Actual sprint dates must come from your course timetable.
2. List prepared cards with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-access-task.ps1 -List`.
3. Copy a title using `-TaskId ACC04-01 -TitleOnly`; paste as the real card title. Run without `-TitleOnly` and paste the full description. `-NoClipboard` prints text instead.
4. Set the actual assignee, prerequisites, status and current dates. Link the actual new branch and PR. Implemented code can be described as existing work; do not pretend the task existed before the recorded creation date.
5. Keep the contract-review and new remote-CI cards open until their actual evidence exists. Record the new PR link with the optional `-PrUrl` argument when preparing card text.

The helper creates no remote card, sends no message and does not certify review or CI. A Story card is acceptable if that is how your board models an implementable use case; the content, links and verifiable contribution matter more than a label.

## Source-to-use-case mapping

| Assigned requirement | Specification source | Local use cases and platform relationship | Order / provider boundary |
|---|---|---|---|
| ACC-04 | §4.2; authorization rules §2.1 | Read concrete own resource; reject ID substitution; authorize draft write. These checks support UC-01/UC-02 and the scoped read portion of UC-04; full start/submission/evaluation flows are owned elsewhere and are not claimed | ACC04-01 → ACC04-02 → ACC04-03; Identity, membership and ATT ports |
| ADM-08 | §4.12; separation of roles in §2 | View authorized course administration; delegate limited administration; update course metadata; revoke administrative delegation. These are derived ADM-08 use cases; they do not claim UC-05 account/session revocation | ADM08-01 → ADM08-02 → ADM08-03; current account and administrative store ports |

For actual card/PR/review recording commands see `CLOSURE.md` and `scripts/record-kt1-evidence.ps1`. Every student's real contribution must be linked; no remote evidence is inferred from this backlog.
