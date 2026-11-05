# Unified domain and access matrix

| Entity / snapshot | Key fields and states | Data owner |
|---|---|---|
| AccountSnapshot | Id, IsActive | Identity provider; synthetic fixture for KT1 |
| MembershipSnapshot | AccountId, CourseId, Student/Teacher/Assistant, IsActive | Course membership provider |
| AttemptSnapshot | Id, OwnerId, CourseId, InProgress/Submitted/Completed, Version | ATT provider; ownership is never a client parameter |
| AttemptView | Id, Answer, Version | ATT content returned only after authorization |
| CourseSnapshot | Id, DisplayName, Version | Administrative metadata adapter; actual course contract must be reviewed |
| AdminGrant | Id, CourseId, GranteeId, GrantedBy, Capabilities, Active/Revoked, Version, IsRoot | ADM-08; no membership/teaching-role assignment |
| SecurityEvent | Time, correlation, actor, action, target, course, outcome, reason, policy version, before/after versions | Metadata-only audit; bounded process-local simulator |

Attempt transitions are consumed from ATT. This contribution modifies only an InProgress answer and advances Version; it does not expose Submit or Complete. Root admin grants are provisioned only by the deterministic seed. A regular administrative grant can transition from absent → Active → Revoked. Regrant creates a new grant ID. Revoked authority cannot be reused for a read or write.

## Subject–Action–Resource–Allow/Deny

All Allow rows require a current active account, a valid resource snapshot and all applicable course checks. Any missing/invalid state or provider failure denies.

| Subject | Action | Resource / condition | Decision |
|---|---|---|---|
| Anonymous | Any protected operation | Any | Deny / 401 |
| Active Student owner + active same-course membership | attempt.read | Own attempt | Allow |
| Other student | attempt.read / attempt.draft.update | Another owner's attempt | Deny / 404 |
| Same-course Teacher | attempt.read | Submitted or Completed attempt | Allow |
| Same-course Teacher | attempt.read | InProgress attempt of a student | Deny / 404 |
| Teacher from another course / Assistant without review authority | attempt.read | Student attempt | Deny / 404 |
| Active Student owner | attempt.draft.update | InProgress + valid input + matching version | Allow; Version + 1 |
| Owner | attempt.draft.update | Submitted / Completed | Deny / 404 |
| Authorized owner | attempt.draft.update | Stale version | Deny write / 409 |
| Root administrator in course A | course.admin.read / settings.update | Course A | Allow |
| Root administrator in course A | Administrative actions | Course B | Deny / 404 |
| Root administrator in course A | delegation.create / revoke | Active non-root grant in A; allowed target/capabilities/version | Allow |
| Active delegate with ViewAdministration | course.admin.read | Granted course only | Allow |
| Active delegate with ManageSettings | course.settings.update | Granted course + matching course version | Allow |
| View-only delegate | course.settings.update | Any course | Deny / 404 |
| Any delegate | delegation.create / revoke | No root delegation power | Deny / 404 |
| Revoked delegate / blocked account | Administrative actions | Any course | Deny / 404 |
| Administrator / delegate without teaching membership | attempt.read / update | Student answer | Deny / 404 |
| Teacher without administrative grant | Administrative actions | Even own teaching course | Deny / 404 |
| Any caller | Unknown action | Any | Shared engine Deny |

Input validation returns 400 for syntactically invalid requests, independently of existence. An authorized caller can receive 409 for a version/active-grant conflict. Foreign/missing resources share exactly `{"code":"RESOURCE_UNAVAILABLE"}` with HTTP 404, including upstream outages. Internal event reasons are not included in these HTTP responses. This is response-shape protection, not a constant-time execution guarantee.

See `diagrams/access-admin-domain.svg` and `access-admin-flow.svg`.
