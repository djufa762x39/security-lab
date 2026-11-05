param([string]$BaseUrl = "http://localhost:5080")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.CookieContainer = New-Object System.Net.CookieContainer
$client = New-Object System.Net.Http.HttpClient($handler)
$client.BaseAddress = [Uri]$BaseUrl
$client.Timeout = [TimeSpan]::FromSeconds(20)
$delegateHandler = New-Object System.Net.Http.HttpClientHandler
$delegateHandler.CookieContainer = New-Object System.Net.CookieContainer
$delegateClient = New-Object System.Net.Http.HttpClient($delegateHandler)
$delegateClient.BaseAddress = [Uri]$BaseUrl
$delegateClient.Timeout = [TimeSpan]::FromSeconds(20)
$script:delegateSignIns = 0
$results = New-Object 'System.Collections.Generic.List[object]'
$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "TestResults"
$null = New-Item -ItemType Directory -Force -Path $outDir
$outPath = Join-Path $outDir "acc04-adm08-http-demo.json"
if (Test-Path -LiteralPath $outPath) { Remove-Item -LiteralPath $outPath }
function Send([string]$Method, [string]$Path, $Body = $null, [bool]$Header = $true, [System.Net.Http.HttpClient]$Session = $client) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "/api/kt1$Path")
    if ($Method -ne "GET" -and $Header) { $request.Headers.Add("X-Kt1-Demo", "1") }
    if ($null -ne $Body) {
        $json = ConvertTo-Json -InputObject $Body -Compress -Depth 5
        $request.Content = [System.Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, "application/json")
    }
    try {
        $response = $Session.SendAsync($request).GetAwaiter().GetResult()
        try { return [pscustomobject]@{ Status = [int]$response.StatusCode; Text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() } }
        finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Login([string]$Persona, [System.Net.Http.HttpClient]$Session = $client) {
    $response = Send "POST" "/session/$Persona" -Session $Session
    if ($response.Status -ne 200) { throw "Session failed (HTTP $($response.Status)). Start the AccessKt1 profile." }
    if ([object]::ReferenceEquals($Session, $delegateClient)) { $script:delegateSignIns++ }
}
function Check([string]$Name, [int]$Expected, [string]$Method, [string]$Path, $Body = $null, [bool]$Header = $true, [System.Net.Http.HttpClient]$Session = $client) {
    $response = Send $Method $Path $Body $Header -Session $Session
    if ($response.Status -ne $Expected) { throw "$Name : expected $Expected, got $($response.Status). $($response.Text)" }
    if ($Expected -eq 404 -and $response.Text -ne '{"code":"RESOURCE_UNAVAILABLE"}') { throw "$Name : inconsistent public denial." }
    $results.Add([pscustomobject]@{ scenario = $Name; status = $response.Status; passed = $true })
    Write-Host "PASS $Name : HTTP $($response.Status)"
    if ($response.Text) { return $response.Text | ConvertFrom-Json }
}
$ana = "/attempts/30000000-0000-0000-0000-000000000001"
$marko = "/attempts/30000000-0000-0000-0000-000000000002"
$draft = "/attempts/30000000-0000-0000-0000-000000000004"
$completed = "/attempts/30000000-0000-0000-0000-000000000005"
$missing = "/attempts/30000000-0000-0000-0000-000000000099"
$courseA = "/courses/20000000-0000-0000-0000-000000000001"
$courseB = "/courses/20000000-0000-0000-0000-000000000002"
$delegateId = "10000000-0000-0000-0000-000000000009"
try {
    $null = Check "Anonymous read" 401 "GET" $ana
    $null = Check "Anonymous write" 401 "PUT" "$draft/answer" @{answer="x";expectedVersion=1}
    Login "ana"
    $null = Check "ACC-04 owner read" 200 "GET" $ana
    $null = Check "Foreign attempt hidden" 404 "GET" $marko
    $null = Check "Missing attempt hidden" 404 "GET" $missing
    $before = Check "Read own draft" 200 "GET" $draft
    $saved = Check "Update own draft" 200 "PUT" "$draft/answer" @{answer="KT1 stored draft";expectedVersion=$before.version}
    if ($saved.version -ne $before.version + 1) { throw "Draft version did not advance." }
    $readback = Check "Read persisted draft" 200 "GET" $draft
    if ($readback.answer -ne "KT1 stored draft") { throw "Draft was not persisted." }
    $null = Check "Stale draft rejected" 409 "PUT" "$draft/answer" @{answer="stale";expectedVersion=$before.version}
    $null = Check "Submitted history immutable" 404 "PUT" "$ana/answer" @{answer="x";expectedVersion=1}
    $null = Check "Completed history immutable" 404 "PUT" "$completed/answer" @{answer="x";expectedVersion=1}
    $null = Check "Invalid draft rejected" 400 "PUT" "$draft/answer" @{answer=" ";expectedVersion=$saved.version}
    $null = Check "Missing write header rejected" 400 "PUT" "$draft/answer" @{answer="x";expectedVersion=$saved.version} $false
    Login "marko"
    $null = Check "IDOR changed ID denied" 404 "GET" $ana
    $null = Check "Foreign draft write denied" 404 "PUT" "$draft/answer" @{answer="x";expectedVersion=999}
    Login "teacher"
    $null = Check "Teacher reads submitted answer in own course" 200 "GET" $ana
    $null = Check "Teacher cannot read unsubmitted answer" 404 "GET" $draft
    $null = Check "Teacher cannot edit student draft" 404 "PUT" "$draft/answer" @{answer="x";expectedVersion=$saved.version}
    $null = Check "Teacher has no administrative grant" 404 "GET" "$courseA/administration"
    Login "foreign-teacher"
    $null = Check "Other-course teacher denied" 404 "GET" $ana
    Login "blocked"
    $null = Check "Blocked account denied" 404 "GET" $ana
    Login "assistant"
    $null = Check "Assistant has no implicit review power" 404 "GET" $ana
    Login "admin"
    $null = Check "Administrator has no answer bypass" 404 "GET" $ana
    $admin = Check "ADM-08 admin reads course A" 200 "GET" "$courseA/administration"
    $null = Check "Course A admin cannot administer B" 404 "GET" "$courseB/administration"
    $null = Check "Self-delegation rejected" 400 "POST" "$courseA/delegations" @{granteeId="10000000-0000-0000-0000-000000000007";capabilities=@("ViewAdministration")}
    $null = Check "Teaching capability cannot be delegated" 400 "POST" "$courseA/delegations" @{granteeId=$delegateId;capabilities=@("Teacher")}
    $grant = Check "Create limited delegation" 200 "POST" "$courseA/delegations" @{granteeId=$delegateId;capabilities=@("ViewAdministration","ManageSettings")}
    $null = Check "Duplicate active grant rejected" 409 "POST" "$courseA/delegations" @{granteeId=$delegateId;capabilities=@("ViewAdministration")}
    Login "delegate" -Session $delegateClient
    $issuedCookie = $delegateHandler.CookieContainer.GetCookies([Uri]$BaseUrl)["SecurityLab.CourseworkKt1"]
    if ($null -eq $issuedCookie) { throw "Delegate session cookie was not issued." }
    $issuedCookieValue = $issuedCookie.Value
    $view = Check "Delegate reads course A" 200 "GET" "$courseA/administration" -Session $delegateClient
    $updated = Check "Delegate updates course settings" 200 "PUT" "$courseA/settings" @{displayName="KT1 delegated course A";expectedVersion=$view.version} -Session $delegateClient
    if ($updated.version -ne $view.version + 1) { throw "Course version did not advance." }
    $null = Check "Stale course update rejected" 409 "PUT" "$courseA/settings" @{displayName="stale";expectedVersion=$view.version} -Session $delegateClient
    $null = Check "Delegate cannot administer course B" 404 "GET" "$courseB/administration" -Session $delegateClient
    $null = Check "Delegate cannot read student answer" 404 "GET" $ana -Session $delegateClient
    $null = Check "Delegate cannot redelegate" 404 "POST" "$courseA/delegations" @{granteeId="10000000-0000-0000-0000-000000000002";capabilities=@("ViewAdministration")} -Session $delegateClient
    Login "other-admin"
    $null = Check "Other admin reads own course B" 200 "GET" "$courseB/administration"
    $null = Check "Other admin cannot revoke course A grant" 404 "POST" "$courseB/delegations/$($grant.id)/revoke" @{expectedVersion=$grant.version}
    Login "admin"
    $null = Check "Stale revocation rejected" 409 "POST" "$courseA/delegations/$($grant.id)/revoke" @{expectedVersion=999}
    $revoked = Check "Revoke delegation" 200 "POST" "$courseA/delegations/$($grant.id)/revoke" @{expectedVersion=$grant.version}
    if ($revoked.state -ne "Revoked") { throw "Revocation not stored." }
    # Reuse the delegate client authenticated BEFORE the separate administrator revoked the grant.
    $null = Check "Existing session denied immediately after revoke" 404 "GET" "$courseA/administration" -Session $delegateClient
    $null = Check "Revoked delegate cannot write settings" 404 "PUT" "$courseA/settings" @{displayName="forbidden";expectedVersion=$updated.version} -Session $delegateClient
    $afterCookie = $delegateHandler.CookieContainer.GetCookies([Uri]$BaseUrl)["SecurityLab.CourseworkKt1"]
    if ($script:delegateSignIns -ne 1 -or $null -eq $afterCookie -or $afterCookie.Value -cne $issuedCookieValue) {
        throw "Revocation proof must reuse the same cookie with exactly one delegate sign-in."
    }
    $record = [ordered]@{ requirements=@("ACC-04","ADM-08"); completed=$true; executedAt=[DateTimeOffset]::UtcNow.ToString("o"); total=$results.Count; passed=$results.Count; revocationProof=@{separateClients=$true;delegateSignIns=$script:delegateSignIns;samePreRevocationCookie=$true}; scenarios=$results.ToArray() }
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outPath -Encoding UTF8
    Write-Host "DEMO PASS: $($results.Count)/$($results.Count). Evidence: TestResults/acc04-adm08-http-demo.json"
} finally { $delegateClient.Dispose(); $delegateHandler.Dispose(); $client.Dispose(); $handler.Dispose() }
