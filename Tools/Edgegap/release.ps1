# One command for a new dedicated server release on Edgegap (plan section 4). Run "Build Linux server (Edgegap)" in
# the Setup window first. From the repo root:
#   ./Tools/Edgegap/release.ps1 -Version 0.1.3
# Steps:
#   1. builds and pushes the image <Registry>:<Version> (push.ps1);
#   2. makes Edgegap app version <Version> as a copy of the allocator's current version (ports, CPU, memory, ...) with
#      the new image tag; with -InPlace <name> it points that existing version at the new image instead (when the
#      account's version limit is reached);
#   3. sets the Cloud Code allocator to the version it should start (EdgegapAllocator.cs).
# Left for you: deploy the Cloud Code module in the editor (Services → Deployment) to development, then production.
# Secrets come from the SecretStore vault "Local": EdgegapToken (API), EdgegapRegistryUser and EdgegapRegistryToken.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$InPlace,
    [string]$App = 'airwingnitro',
    [string]$Registry = 'registry.edgegap.com/superhotdancergames-wffvi2kvblrl/airwing-server',
    [switch]$SkipPush
)
$ErrorActionPreference = 'Stop'
$api = "https://api.edgegap.com/v1/app/$App/version"
$allocator = Join-Path $PSScriptRoot '../../CloudCode/ChopperAllocator/Project/EdgegapAllocator.cs'
$versionPattern = 'private const string Version = "([^"]+)";'

# The dashboard shows the key as "token <key>"; accept the secret stored with or without that word.
$headers = @{ Authorization = "token $((Get-Secret EdgegapToken -AsPlainText) -replace '^\s*token\s+', '')" }
try { Invoke-RestMethod -Uri 'https://api.edgegap.com/v1/apps' -Headers $headers | Out-Null }
catch { throw "Edgegap refused the API token (vault 'Local', EdgegapToken): $($_.Exception.Message)" }

# 1. Image.
if (-not $SkipPush) {
    & (Join-Path $PSScriptRoot 'push.ps1') -Registry $Registry -Version $Version
    if (-not $?) { throw 'push failed' }
}

# 2. App version.
# File API, not Get/Set-Content: Windows PowerShell 5.1 reads and writes the UTF-8 source as ANSI.
$allocator = [IO.Path]::GetFullPath($allocator)
$code = [IO.File]::ReadAllText($allocator)
$current = [regex]::Match($code, $versionPattern).Groups[1].Value
if ($InPlace) {
    Invoke-RestMethod -Method Patch -Uri "$api/$InPlace" -Headers $headers -ContentType 'application/json' `
        -Body (@{ docker_tag = $Version } | ConvertTo-Json) | Out-Null
    $target = $InPlace
    Write-Host "Edgegap version $InPlace now runs image tag $Version"
}
else {
    $source = Invoke-RestMethod -Uri "$api/$current" -Headers $headers
    $body = @{}
    foreach ($property in $source.PSObject.Properties) { $body[$property.Name] = $property.Value }
    foreach ($readOnly in 'create_time', 'last_updated', 'id') { $body.Remove($readOnly) }
    $body.name = $Version
    $body.docker_tag = $Version
    # Edgegap returns the registry login masked; send the real one.
    $body.private_username = Get-Secret EdgegapRegistryUser -AsPlainText
    $body.private_token = Get-Secret EdgegapRegistryToken -AsPlainText
    Invoke-RestMethod -Method Post -Uri $api -Headers $headers -ContentType 'application/json' `
        -Body ($body | ConvertTo-Json -Depth 8) | Out-Null
    $target = $Version
    Write-Host "Edgegap version $Version made from $current"
}

# 3. Allocator.
[IO.File]::WriteAllText($allocator, ($code -replace $versionPattern, "private const string Version = `"$target`";"), (New-Object Text.UTF8Encoding $false))
Write-Host "Allocator now starts version $target."
Write-Host 'Next: Services -> Deployment -> deploy ChopperAllocator.ccmr to development, test, then to production.'
