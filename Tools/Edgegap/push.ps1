# Builds the server image from Builds/LinuxServer and pushes it to the Edgegap registry (plan section 4).
# Run "Build Linux server (Edgegap)" in the Setup window first. Log in once per machine with the registry
# credentials from the Edgegap dashboard (Container Registry):
#   docker login registry.edgegap.com
# Then, from the repo root:
#   ./Tools/Edgegap/push.ps1 -Registry registry.edgegap.com/<org>/<app> -Version 0.1.0
# Use the same version as the Edgegap app version's tag; map port 7979/UDP there.
param(
    [Parameter(Mandatory = $true)][string]$Registry,
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'

$build = Join-Path $PSScriptRoot '../../Builds/LinuxServer'
if (-not (Test-Path (Join-Path $build 'AirwingServer.x86_64'))) { throw "No server build in $build" }

$image = "${Registry}:$Version"
docker build --platform linux/amd64 -t $image $build
if ($LASTEXITCODE -ne 0) { throw 'docker build failed' }
docker push $image
if ($LASTEXITCODE -ne 0) { throw 'docker push failed (run "docker login registry.edgegap.com" first?)' }
Write-Host "Pushed $image"
