# Saves the match logs of running Edgegap deployments (app airwingnitro) to Logs/Matches/<request id>.jsonl.
# Edgegap keeps container logs only while a deployment runs (unless Endpoint Storage is set up on the app version),
# and the server stops itself when idle, so run this before the last player leaves, or with -Watch during a test:
#   ./Tools/Edgegap/fetch-logs.ps1            # once
#   ./Tools/Edgegap/fetch-logs.ps1 -Watch 60  # every 60 s until stopped (Ctrl+C)
# Each file is replaced with the full log every time, so the last fetch before a stop has the most.
# The full container log (also Unity's own lines) goes next to it as <request id>.log.
# Secret: EdgegapToken in the SecretStore vault "Local".
param(
    [string]$App = 'airwingnitro',
    [int]$Watch = 0
)
$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "token $((Get-Secret EdgegapToken -AsPlainText) -replace '^\s*token\s+', '')" }
$out = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Logs/Matches'))
New-Item -ItemType Directory -Force $out | Out-Null
$prefix = '[Event] '

function Save-Once {
    $deployments = (Invoke-RestMethod -Uri 'https://api.edgegap.com/v1/deployments' -Headers $headers).data
    $mine = @($deployments | Where-Object { $_.app_name -eq $App -or -not $_.app_name })
    if ($mine.Count -eq 0) { Write-Host "$(Get-Date -Format T) no running deployment"; return }
    foreach ($deployment in $mine) {
        $id = $deployment.request_id
        try {
            $logs = (Invoke-RestMethod -Uri "https://api.edgegap.com/v1/deployment/$id/container-logs" -Headers $headers).logs
        }
        catch {
            Write-Warning "$id : $($_.Exception.Message)"
            continue
        }
        $lines = $logs -split "`r?`n"
        $events = $lines | Where-Object { $_.StartsWith($prefix) } | ForEach-Object { $_.Substring($prefix.Length) }
        [IO.File]::WriteAllLines((Join-Path $out "$id.jsonl"), [string[]]@($events), (New-Object Text.UTF8Encoding $false))
        [IO.File]::WriteAllText((Join-Path $out "$id.log"), $logs, (New-Object Text.UTF8Encoding $false))
        Write-Host "$(Get-Date -Format T) $id : $(@($events).Count) events"
    }
}

do {
    Save-Once
    if ($Watch -gt 0) { Start-Sleep -Seconds $Watch }
} while ($Watch -gt 0)
