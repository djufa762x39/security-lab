param(
    [switch]$Status,
    [string]$TaskId, [string]$TapizId, [string]$TapizUrl,
    [string]$PrUrl, [string]$CiRunUrl, [string]$TestedCommit,
    [string]$Reviewer, [string]$ContractReviewUrl, [datetime]$ReviewDate,
    [string]$ContributorName, [string]$ContributionUrl
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$evidencePath = Join-Path $root "docs/components/coursework/EVIDENCE.json"
$backlogPath = Join-Path $root "docs/components/coursework/backlog.json"
$evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
$backlog = Get-Content -LiteralPath $backlogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$external = $evidence.externalEvidence
function Set-Field($Object, [string]$Name, $Value) {
    $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value -Force
}
function Check-Https([string]$Value) {
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne "https" -or $uri.UserInfo) {
        throw "Upisi stvarni HTTPS link bez kredencijala u adresi."
    }
}
$hasTask = $TaskId -or $TapizId -or $TapizUrl
$hasReview = $Reviewer -or $ContractReviewUrl -or $PSBoundParameters.ContainsKey("ReviewDate")
$hasContributor = $ContributorName -or $ContributionUrl
$hasWrite = $hasTask -or $PrUrl -or $CiRunUrl -or $TestedCommit -or $hasReview -or $hasContributor
if ($Status) {
    if ($hasWrite) { throw "Koristi -Status odvojeno od parametara za upis." }
    $rows = @(
        [pscustomobject]@{ Evidence="Tapiz kartice"; Recorded="$(@($backlog.tasks | Where-Object { $_.tapizId -and $_.tapizUrl }).Count)/$($backlog.tasks.Count)" },
        [pscustomobject]@{ Evidence="Novi PR"; Recorded=[bool]$external.newPrUrl },
        [pscustomobject]@{ Evidence="CI link i testirani commit"; Recorded=[bool]($external.ciRunUrl -and $external.testedCommit) },
        [pscustomobject]@{ Evidence="Pregled ugovora"; Recorded=[bool]$external.neighborContractReview },
        [pscustomobject]@{ Evidence="Licni doprinosi"; Recorded=@($external.contributors).Count }
    )
    $rows | Format-Table -AutoSize
    Write-Host "Ovo je evidencija unetih referenci. Sadrzaj linkova, stvaran review, autori i remote CI nisu automatski potvrdeni."
    return
}
if (-not $hasWrite) { throw "Koristi -Status ili parametre za stvarne linkove. Pogledaj CLOSURE.md." }
# Validate every supplied group before changing either file.
if ($hasTask) {
    if ([string]::IsNullOrWhiteSpace($TaskId) -or [string]::IsNullOrWhiteSpace($TapizId) -or [string]::IsNullOrWhiteSpace($TapizUrl)) { throw "Za karticu navedi -TaskId, -TapizId i -TapizUrl zajedno." }
    Check-Https $TapizUrl
    $task = @($backlog.tasks | Where-Object { $_.id -eq $TaskId })
    if ($task.Count -ne 1) { throw "Nepoznat TaskId. Pokreni prepare-access-task.ps1 -List." }
    $task = $task[0]
}
if ($PrUrl -and $PrUrl -notmatch '^https://github\.com/owlCoder/security-lab/pull/[1-9][0-9]*$') { throw "PrUrl mora biti stvarni novi security-lab PR." }
if ($CiRunUrl -and $CiRunUrl -notmatch '^https://github\.com/owlCoder/security-lab/actions/runs/[1-9][0-9]*(/attempts/[1-9][0-9]*)?$') { throw "CiRunUrl mora biti direktan link na Actions run." }
if ($TestedCommit -and $TestedCommit -notmatch '^[0-9a-fA-F]{40}$') { throw "TestedCommit mora biti puni SHA od 40 znakova." }
if ($CiRunUrl -and -not ($TestedCommit -or $external.testedCommit)) { throw "Uz CI link navedi -TestedCommit sa stvarnog run-a." }
if ($hasReview) {
    if ([string]::IsNullOrWhiteSpace($Reviewer) -or [string]::IsNullOrWhiteSpace($ContractReviewUrl) -or -not $PSBoundParameters.ContainsKey("ReviewDate")) { throw "Za review navedi -Reviewer, -ContractReviewUrl i -ReviewDate zajedno." }
    Check-Https $ContractReviewUrl
}
if ($hasContributor) {
    if ([string]::IsNullOrWhiteSpace($ContributorName) -or [string]::IsNullOrWhiteSpace($ContributionUrl)) { throw "Navedi -ContributorName i -ContributionUrl zajedno." }
    Check-Https $ContributionUrl
}
$recordedAt = [DateTimeOffset]::UtcNow.ToString("o")
if ($hasTask) {
    $task.tapizId = $TapizId
    $task.tapizUrl = $TapizUrl
    $links = @($external.tapizTaskLinks | Where-Object { $_.taskId -ne $TaskId })
    $links += [pscustomobject]@{taskId=$TaskId; tapizId=$TapizId; url=$TapizUrl; recordedAt=$recordedAt}
    $external.tapizTaskLinks = $links
}
if ($PrUrl) { $external.newPrUrl = $PrUrl }
if ($CiRunUrl) { $external.ciRunUrl = $CiRunUrl; $external.ciArtifactConfirmed = $false }
if ($TestedCommit) { $external.testedCommit = $TestedCommit.ToLowerInvariant() }
if ($hasReview) {
    $external.neighborContractReview = [pscustomobject]@{
        reviewer=$Reviewer; url=$ContractReviewUrl; reviewedOn=$ReviewDate.ToString("yyyy-MM-dd"); recordedAt=$recordedAt;
        status="REFERENCE_RECORDED_CONTENT_REQUIRES_REVIEW"
    }
}
if ($hasContributor) {
    $contributors = @($external.contributors | Where-Object { $_.name -ne $ContributorName })
    $contributors += [pscustomobject]@{name=$ContributorName; evidenceUrl=$ContributionUrl; recordedAt=$recordedAt}
    Set-Field $external "contributors" $contributors
}
Set-Field $external "referenceStatus" "RECORDED_BY_USER_NOT_AUTOMATICALLY_VERIFIED"
$encoding = New-Object System.Text.UTF8Encoding($false)
if ($hasTask) { [IO.File]::WriteAllText($backlogPath, ($backlog | ConvertTo-Json -Depth 20) + [Environment]::NewLine, $encoding) }
[IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 20) + [Environment]::NewLine, $encoding)
Write-Host "Sacuvane su stvarne reference koje si unela. Remote kartice nisu kreirane, review nije poslat i CI uspeh nije automatski oznacen."
