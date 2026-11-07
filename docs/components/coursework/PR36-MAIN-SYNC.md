# PR #36 — second current-main integration

Team 8 scope: **ACC-04 / ADM-08**. Feature input: `3d751274493141202f85c3d686378aedada012c2`. Main input: `0a4f3d1325ef340f19dea25d230209105dfa67b0`.

The previous merge integrated main `8420f3b`. Subsequent main merges #51, #48, #28 and #47 introduced audit records, assignment work, Identity rate limiting and the laboratory scaffold. This second normal merge retains all of that work.

## Conflict resolution

Only `backend/Platform.Api/Program.cs` conflicted. The resolved file retains all current-main imports, registrations, middleware and routes, and composes the explicitly enabled Development-only KT1 services/routes. `AddLaboratories`, `MapLaboratories` and `AddIdentityRateLimiting` remain present. `UseIdentityRateLimiting` runs before authentication as required by its existing module. The solution automatically merges both new main test projects and the existing KT1 project without duplicates.

No blanket ours/theirs resolution, old-PR replay or force push is used. The shared ACC-05/ACC-08 module is unchanged; no ACC-08 contextual-policy implementation is introduced.

Two additions to `MainCompositionTests.cs` check laboratory authorization/identity isolation with KT1 enabled and failure to start with invalid rate-limit configuration. Local verification: **317/317** solution cases, **65/65** KT1 cases, live demo **41/41**, frontend build PASS. These are simulator coexistence checks, not production provider integration.

## Publication

Use the delivered guarded PowerShell script to create an isolated `fix/pr36-main-sync-2` worktree, perform the real merge and verify the full Git tree. Existing local files and staging remain untouched. After local checks, make a normal merge commit and push `HEAD:feature/acc04-adm08-kt1`. This updates **the same PR #36**. The next remote CI result must be verified after that push.
