<#
.SYNOPSIS
    One-command demo: starts the Trivia Battle server and opens every screen on this PC.

.DESCRIPTION
    No PLC or other hardware needed. Each player screen is its own keypad: click into a player
    window and press 1-4 (or A-D, or tap an answer) to answer as that player. Set up the match at
    the kiosk window ("Simulate card swipe" or "Play as guest").

    The server opens in its own window; close that window (or press Ctrl+C in it) to stop.
    Close the screen windows with:  .\tools\launch-screens.ps1 -Close

.PARAMETER ResetDatabase
    Deletes the local database first, so it's rebuilt with the starter questions from
    src/TriviaBattle.Server/Data/Seed. This ALSO deletes match history, leaderboards and players.

.EXAMPLE
    .\tools\demo.ps1
    .\tools\demo.ps1 -ResetDatabase
#>
param(
    [switch]$ResetDatabase
)

$repoRoot = Split-Path $PSScriptRoot -Parent
$serverFolder = Join-Path $repoRoot 'src\TriviaBattle.Server'
$serverUrl = 'http://localhost:5000'

if ($ResetDatabase) {
    $database = Join-Path $serverFolder 'triviabattle.db'
    Remove-Item "$database", "$database-wal", "$database-shm" -ErrorAction SilentlyContinue
    Write-Host 'Database removed; it will be rebuilt with the starter questions.'
}

Write-Host 'Starting the server (in its own window)...'
Start-Process -FilePath 'dotnet' -ArgumentList 'run' -WorkingDirectory $serverFolder

# Wait until the server answers (the first build can take a minute).
$deadline = (Get-Date).AddSeconds(120)
while ($true) {
    try {
        Invoke-RestMethod "$serverUrl/api/rooms" -TimeoutSec 2 | Out-Null
        break
    } catch {
        if ((Get-Date) -gt $deadline) { throw "The server didn't start within 2 minutes. Check its window for errors." }
        Start-Sleep -Seconds 1
    }
}

Write-Host 'Server is up. Opening the screens...'
& (Join-Path $PSScriptRoot 'launch-screens.ps1') -Server $serverUrl -Demo

Write-Host ''
Write-Host 'Demo running:'
Write-Host '  - Kiosk window: pick a mode, add players (Simulate card swipe / Play as guest), start.'
Write-Host '  - Player windows: click into one, then press 1-4 (or A-D, or tap an answer) to answer as that player.'
Write-Host "  - Admin page: $serverUrl/admin"
