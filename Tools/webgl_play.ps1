<#
Runs the WebGL build in a real (headed) Chrome on the real GPU, waits for it to actually start
playing, then grabs the browser window.

Headless Chrome screenshots fire the moment the page "loads", which for a Unity build is while
the WASM is still compiling — you get the loading bar every time. And software rendering says
nothing about whether the game runs at 60 fps on a GPU. So this drives a real window and
captures it after a genuine wall-clock delay.

    powershell -File Tools\webgl_play.ps1 -Seconds 45
#>
param(
    [int]$Seconds = 45,
    # A BURST, not a snapshot. Three frames six seconds apart covers twelve seconds of a
    # seventy-eight second match, so which beat you photograph is decided by how long the build
    # happened to spend compiling wasm — and that changes with every build. Ten frames ten seconds
    # apart covers a whole match from wherever it starts, which is the difference between a review
    # tool and a lottery.
    [int]$Shots = 10,
    [int]$Interval = 10,
    [int]$Port = 8123,
    [string]$Out = "C:\Duck\Captures\WebGL",
    [string]$Build = ""
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$serve = Start-Process -FilePath "python" `
    -ArgumentList @(@("C:\Duck\Tools\webgl_test.py", "--serve-only", "--port", $Port) + $(if ($Build) { @("--build", $Build) } else { @() })) `
    -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2

$launchedAt = Get-Date
$url = "http://127.0.0.1:$Port/index.html"
$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$profile = Join-Path $env:TEMP ("duckplay_" + [guid]::NewGuid().ToString("N"))

# --app strips the browser chrome so the capture is just the game.
$browser = Start-Process -FilePath $chrome -PassThru -ArgumentList @(
    "--app=$url",
    "--window-size=1280,720",
    "--window-position=0,0",
    "--user-data-dir=$profile",
    "--no-first-run",
    "--no-default-browser-check",
    "--autoplay-policy=no-user-gesture-required",
    "--disable-features=CalculateNativeWinOcclusion",
    # The browser console, written to disk.
    #
    # This harness drives a REAL window because headless screenshots fire while the wasm is still
    # compiling — but a real window also meant no console, so when the player did something
    # unexpected there was nothing to read and three separate theories got tested against the build
    # output instead of against what the game actually said. A development player forwards
    # Debug.Log here; a release one does not, which is itself worth knowing.
    "--enable-logging",
    "--log-level=0"
)

Write-Output "waiting $Seconds s for the build to load and start playing..."
Start-Sleep -Seconds $Seconds

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# Capture the BROWSER WINDOW, not a rectangle of the desktop.
#
# This grabbed the top-left 1280x720 of the primary screen and called it the game. Whatever
# happened to be sitting at that position got photographed instead — another Chrome window, the
# editor, a previous run — and the failure is silent and extremely convincing: the frames look
# like a real Unity build doing the wrong thing. Two hours went into three theories about why an
# arena-first player was "opening on the menu", and the player had been running the arena the
# whole time; the harness was pointed at a different window.
#
# So the window is found by its own handle, raised, and measured. If it cannot be found the run
# fails loudly rather than returning a plausible photograph of something else.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public class Win {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@

# Find OUR window, not any Chrome window.
#
# Start-Process returns Chrome's launcher, and the launcher usually has no window of its own — so
# MainWindowHandle is zero and the obvious fallback, "first chrome process with a window", picks
# whichever Chrome happens to be open. A stale window left by an earlier run of this very script
# is exactly what it picks, and it is showing an older build. That is the second time the same
# review loop has photographed the wrong thing while the game underneath was fine.
#
# So: only windows belonging to Chrome processes started AFTER this run began, newest first.
$browser.Refresh()
$hwnd = $browser.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) {
    $mine = Get-Process chrome -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $launchedAt -and $_.MainWindowHandle -ne [IntPtr]::Zero } |
            Sort-Object StartTime -Descending
    if ($mine) { $hwnd = $mine[0].MainWindowHandle }
}
if ($hwnd -eq [IntPtr]::Zero) { throw "could not find this run's browser window; refusing to photograph something else" }
[void][Win]::ShowWindow($hwnd, 9)
[void][Win]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 600

# Named for the parameter clash: PowerShell variable names are case-INSENSITIVE, so a local
# $shots array and the [int]$Shots parameter are one variable, and assigning an array to it
# fails at runtime with a type error that points at the wrong line.
$written = @()
for ($i = 1; $i -le $Shots; $i++) {
    $r = New-Object RECT
    [void][Win]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { throw "browser window has no size" }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $path = Join-Path $Out ("webgl_play_{0:00}.png" -f $i)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $written += $path
    Write-Output "captured $path"
    if ($i -lt $Shots) { Start-Sleep -Seconds $Interval }
}

# Keep the console before the profile is deleted with it.
$chromeLog = Join-Path $profile "chrome_debug.log"
if (Test-Path $chromeLog) {
    Copy-Item $chromeLog (Join-Path $Out "browser_console.log") -Force
    Write-Output "console: $(Join-Path $Out 'browser_console.log')"
}

try { $browser | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
try { $serve | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "done"
