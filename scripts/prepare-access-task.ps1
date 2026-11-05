param([string]$TaskId, [switch]$List, [switch]$TitleOnly, [switch]$NoClipboard, [string]$PrUrl = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$data = Get-Content -LiteralPath (Join-Path $root "docs/components/coursework/backlog.json") -Raw -Encoding UTF8 | ConvertFrom-Json
if ($List) { $data.tasks | Select-Object id, phase, implementation, title | Format-Table -AutoSize; return }
if (-not $TaskId) { throw "Use -List or -TaskId ACC04-01." }
$task = @($data.tasks | Where-Object { $_.id -eq $TaskId })
if ($task.Count -ne 1) { throw "Unknown task ID. Use -List." }
if ($PrUrl -and $PrUrl -notmatch '^https://github\.com/owlCoder/security-lab/pull/[1-9][0-9]*$') { throw "Use the actual new security-lab PR URL." }
$task = $task[0]
$title = "$($task.id) - $($task.title)"
$branch = "feature/acc04-adm08-kt1"
if (Test-Path -LiteralPath (Join-Path $root ".git")) {
    $actualBranch = git -C $root branch --show-current
    if ($LASTEXITCODE -eq 0 -and $actualBranch) { $branch = $actualBranch.Trim() }
}
$actualPr = if ($PrUrl) { $PrUrl } else { "PENDING - paste the actual new PR URL" }
$content = @(
    $title, "", "Requirements: $($task.requirements -join ', ')", "Phase: $($task.phase)",
    "Implementation evidence: $($task.implementation)", "Use cases: $($task.useCases -join '; ')", "",
    "Prerequisites / order: $($task.prerequisites)", "", "Acceptance criteria: $($task.acceptance)", "",
    "Evidence: $($task.evidence -join '; ')", "Branch: $branch", "PR: $actualPr", "",
    "Record actual card ID, assignee and dates. This prepared text does not create a remote Tapiz task or prove external review / CI."
) -join [Environment]::NewLine
if ($TitleOnly) { $content = $title }
if ($NoClipboard) { Write-Output $content }
else { Set-Clipboard -Value $content; Write-Host "Copied: $title"; Write-Host "Paste into the real Tapiz card. No remote task was created." }
