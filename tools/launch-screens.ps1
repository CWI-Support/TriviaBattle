<#
.SYNOPSIS
    Opens every Trivia Battle screen in its own full-screen Chromium/Chrome/Edge window.

.DESCRIPTION
    For a PC driving several displays: each screen gets its own browser window in kiosk mode,
    placed on its monitor by screen position (see $Screens below). Screens can equally run on
    separate machines: just open the same URLs there.

    Each window uses its own browser profile folder, which is what lets several kiosk-mode
    windows run side by side. Profiles live in %LOCALAPPDATA%\TriviaBattle\browser-profiles.

    To close everything: run again with -Close, or press Alt+F4 in each window.

.PARAMETER Server
    Base URL of the game server. Default: http://localhost:5000

.PARAMETER Demo
    Opens normal (not full-screen) windows tiled on the main monitor, for trying things out:
    the main screen, the four player screens and the kiosk.

.PARAMETER Close
    Closes the screen windows started by this script.

.EXAMPLE
    .\tools\launch-screens.ps1                                  # venue: full screen on each monitor
    .\tools\launch-screens.ps1 -Demo                            # try it on one monitor
    .\tools\launch-screens.ps1 -Server http://192.168.1.50:5000 # server on another machine
#>
param(
    [string]$Server = 'http://localhost:5000',
    [switch]$Demo,
    [switch]$Close
)

# ---------------------------------------------------------------------------
# EDIT THIS TABLE for your venue.
# X/Y = top-left corner of the monitor the screen should appear on, in Windows desktop
# coordinates (Settings > System > Display shows the arrangement). Portrait player
# monitors should be rotated in Windows display settings; the page adapts by itself.
# ---------------------------------------------------------------------------
$Screens = @(
    @{ Name = 'main';    Path = '/display/main';     X = 0;    Y = 0 }
    @{ Name = 'player1'; Path = '/display/player/1'; X = 1920; Y = 0 }
    @{ Name = 'player2'; Path = '/display/player/2'; X = 3000; Y = 0 }
    @{ Name = 'player3'; Path = '/display/player/3'; X = 4080; Y = 0 }
    @{ Name = 'player4'; Path = '/display/player/4'; X = 5160; Y = 0 }
    # The kiosk is usually its own touchscreen PC outside the room. If it's on this PC, add:
    # @{ Name = 'kiosk'; Path = '/kiosk'; X = 6240; Y = 0 }
)

$ProfileRoot = Join-Path $env:LOCALAPPDATA 'TriviaBattle\browser-profiles'

function Find-Browser {
    $candidates = @(
        "$env:ProgramFiles\Chromium\Application\chrome.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    )
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw 'No Chromium, Chrome or Edge found. Install one, or add its path to Find-Browser.' }
    return $found
}

if ($Close) {
    # Only close browser processes that use our profile folders, never the user's own browser.
    Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe' OR Name = 'msedge.exe'" |
        Where-Object { $_.CommandLine -like "*$ProfileRoot*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Write-Host 'Closed Trivia Battle screen windows.'
    return
}

$browser = Find-Browser
Write-Host "Using $browser"
Write-Host "Server: $Server"

$index = 0
foreach ($screen in $(if ($Demo) { $DemoScreens } else { $Screens })) {
    $commonArgs = @(
        "--user-data-dir=`"$(Join-Path $ProfileRoot $screen.Name)`"",
        '--autoplay-policy=no-user-gesture-required',  # intro video may play with sound
        '--no-first-run',
        '--no-default-browser-check',
        '--disable-session-crashed-bubble',
        '--disable-features=Translate',
        '--noerrdialogs'
    )

    if ($Demo) {
        # Tile small windows: 3 per row on the main monitor.
        $x = ($index % 3) * 640
        $y = [math]::Floor($index / 3) * 520
        $placement = @("--window-position=$x,$y", '--window-size=640,500', "--app=$Server$($screen.Path)")
    } else {
        $placement = @("--window-position=$($screen.X),$($screen.Y)", '--kiosk', "$Server$($screen.Path)")
    }

    Start-Process -FilePath $browser -ArgumentList ($commonArgs + $placement)
    Write-Host ("  {0,-8} {1}{2}" -f $screen.Name, $Server, $screen.Path)
    $index++
}
