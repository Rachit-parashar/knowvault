# Deploys the dev environment. Idempotent — rerunning updates in place.
# Usage: .\scripts\deploy-dev.ps1 -SqlAdminPassword (Get-Content $env:TEMP\kv-sqlpw.txt -Raw)
param(
    [Parameter(Mandatory = $true)]
    [string]$SqlAdminPassword,

    [string]$Location = 'southeastasia'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

az deployment sub create `
    --name "knowvault-dev-$(Get-Date -Format yyyyMMddHHmm)" `
    --location $Location `
    --template-file "$repoRoot\infra\main.bicep" `
    --parameters "$repoRoot\infra\main.dev.bicepparam" `
    --parameters sqlAdminPassword=$SqlAdminPassword `
    --output table
