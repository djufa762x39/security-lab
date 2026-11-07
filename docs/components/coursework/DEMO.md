# KT1 demonstration

Requirements: .NET SDK 10; Node 22.12+ or 24+; Windows PowerShell 5.1 or PowerShell 7. Docker and Tapiz are not required to run this local simulator.

## Terminal 1 — API

```powershell
cd "$env:USERPROFILE\Desktop\security-lab-acc04-adm08"
dotnet run --project ".\backend\Platform.Api\Platform.Api.csproj" --launch-profile AccessKt1
```

Leave it running. API: `http://localhost:5080`. Restart this process to restore the deterministic seed and root grants. The `AccessKt1` launch profile explicitly enables synthetic sessions in Development. Without both conditions, `/api/kt1/*` is absent.

## Terminal 2 — actual HTTP demonstration

```powershell
cd "$env:USERPROFILE\Desktop\security-lab-acc04-adm08"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\kt1-demo.ps1"
```

The script uses real HTTP calls and cookies. It stops on the first unexpected result, clears a previous evidence file before starting, and writes completed evidence only after every scenario passes. It reads the current draft/course versions so the successful flow can be rerun. A failed run after creating a grant may require an API restart before rerunning.

Show: owner read + draft update + persisted read; foreign ID rejected; stale write rejected; submitted/completed history immutable; teacher scoped to submitted course attempts; admin has no answer bypass; course A/B isolation; limited delegation; delegate settings update; no redelegation; cross-course revoke denied; root revoke; existing delegate cookie immediately denied. Administrator and delegate use separate HTTP clients; the delegate signs in exactly once before revocation, and the script checks that its cookie did not change. The JSON proof contains booleans/counters, never the cookie itself.

Evidence: `TestResults/acc04-adm08-http-demo.json`. Structured change audit appears in the API terminal as `KT1_AUDIT_JSON` and contains metadata, not answers.

## Terminal 3 — React frontend

```powershell
cd "$env:USERPROFILE\Desktop\security-lab-acc04-adm08\frontend"
npm.cmd ci
npm.cmd run dev
```

Open `http://localhost:5173`. Keep this terminal running; stop Vite with Ctrl+C before a later `npm ci` on Windows to avoid locked native module files.

Choose account and **Sign in**. ACC-04: select Ana's draft, **Read attempt**, edit, **Save own draft**; switch to Marko and repeat the read. ADM-08: sign in as admin, **Load administration**, **Create delegation**. Keep the grant ID/version shown in the fields, sign in as delegate and change course A settings; try B and an attempt. Sign back in as admin and **Revoke delegation** using the retained ID/version, then repeat the delegate request. The server enforces every rule even if a user calls the API directly.

## Tests and frontend build (stop Vite first)

```powershell
cd "$env:USERPROFILE\Desktop\security-lab-acc04-adm08"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\check-kt1.ps1"
```

This runs `dotnet test backend/SecurityLab.slnx` and the frontend install/build. It does not auto-start the live demo, create Tapiz cards or claim a remote Actions result. See `VALIDATION.md` for the actual packaged validation.

For actual Tapiz, contribution, PR/CI and neighboring-review references, follow `CLOSURE.md`. The `record-kt1-evidence.ps1` helper records user-supplied references without fabricating external completion.
