param([string]$BaseUrl = "http://localhost:5080")
# Compatibility entry point for the user's earlier commands. Runs ACC-04 + ADM-08.
& (Join-Path $PSScriptRoot "kt1-demo.ps1") -BaseUrl $BaseUrl
