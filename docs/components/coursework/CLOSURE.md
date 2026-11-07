# Remaining external KT1 evidence

The code fixes and diagrams are delivered. A saved URL is a reference, not automatic verification that the external requirement is satisfied. This checklist must be completed using actual work, authors and results. No CONTRIBUTION.md is required.

## 1. Actual Tapiz cards

Use the actual team board and create/update the prepared cards. The helper only copies text; it does not create remote cards.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-access-task.ps1 -List
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-access-task.ps1 -TaskId ACC04-01 -TitleOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-access-task.ps1 -TaskId ACC04-01
```

Paste title and description into the actual card, assign the actual student and current work status, and record real dates. Repeat for each applicable prepared task, including ADM-08 and external dependencies. Record each created card with the command below, replacing both sample values with real values before running:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\record-kt1-evidence.ps1 -TaskId ACC04-01 -TapizId "STVARNI_ID_KARTICE" -TapizUrl "STVARNI_HTTPS_LINK_KARTICE"
```

This updates `backlog.json` and `EVIDENCE.json`. Do not backdate task creation or claim that prepared files created remote cards.

## 2. Own contribution and the new PR

Use the actual main-based branch. Each student must show a concrete contribution and a commit, PR or other verifiable record. Link the actual Tapiz card to that work. Do not list authors who did not contribute. The original delivered source is not evidence of each student's own commit.

Record the actual new PR and each student's evidence with the helper's `-PrUrl` and `-ContributorName` / `-ContributionUrl` parameters. See `PR-OPIS.md` outside the repository for the prepared PR description. Record your team's confirmed ACC-04 / ADM-08 assignment in the shared assignment table only with the actual team identifier; do not overwrite the existing Team 3 row.

## 3. Actual GitHub Actions result

Open the new PR, inspect the backend/frontend checks, the test log and the `acc04-adm08-kt1-results` artifact. Record the full SHA tested by that run, not an assumed local SHA. Use `-CiRunUrl` with `-TestedCommit`. Recording a URL deliberately does not set `ciArtifactConfirmed=true`; confirm the real result/artifact before declaring this obligation closed.

A documentation-only evidence commit may lead to another run; the PR's current required checks still need to pass. Local reports must not be presented as GitHub Actions reports.

## 4. Actual neighboring-team review

`CONTRACTS.md` contains a concrete ATT snapshot/CAS proposal and review message. Send it yourself to the actual ATT provider owner, or agree an identity/shared-access boundary with its actual owning team. The neighbor needs to review the contract, not implement their entire module for you.

Record a real acknowledgement with `-Reviewer`, `-ContractReviewUrl` and `-ReviewDate`. The review should identify the contract/version and reviewed code, state positive/negative/unavailable behavior, name who owns the real adapter, and list unresolved questions. A message asking for review is not an acknowledgement.

## 5. Inspect recorded evidence

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\record-kt1-evidence.ps1 -Status
```

The status command reports recorded references only. It does not silently create cards, send messages, certify an approval or turn unverified CI into success. Once references have been checked, update the human-readable review/validation record and commit the documentation to the same PR.

## Scope reminders

The simulator is permitted for KT1. No production database, complete identity system, teaching-role assignment or evaluator implementation is claimed. Permanent-until-revoked administrative delegation is ADM-08; it does not implement temporary teaching permissions or contextual access windows. The shared `Platform.Access` component remains owned by its existing team.
