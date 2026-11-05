$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet test ".\backend\SecurityLab.slnx" --configuration Release --logger "trx" --results-directory ".\TestResults"
    if ($LASTEXITCODE -ne 0) { throw "Backend testovi nisu prosli." }
    Push-Location (Join-Path $root "frontend")
    try {
        npm.cmd ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm ci nije uspeo. Zaustavi Vite pre ponavljanja." }
        npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend build nije uspeo." }
    } finally { Pop-Location }
    Write-Host "Unit/HTTP tests and frontend build passed. Start AccessKt1, then run scripts/kt1-demo.ps1."
} finally { Pop-Location }
