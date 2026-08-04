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
    [int]$Port = 8123,
    [string]$Out = "C:\Duck\Captures\WebGL"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$serve = Start-Process -FilePath "python" `
    -ArgumentList @("C:\Duck\Tools\webgl_test.py", "--serve-only", "--port", $Port) `
    -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2

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
    "--disable-features=CalculateNativeWinOcclusion"
)

Write-Output "waiting $Seconds s for the build to load and start playing..."
Start-Sleep -Seconds $Seconds

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$shots = @()
for ($i = 1; $i -le 3; $i++) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap 1280, 720
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size 1280, 720))
    $path = Join-Path $Out ("webgl_play_{0}.png" -f $i)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $shots += $path
    Write-Output "captured $path"
    if ($i -lt 3) { Start-Sleep -Seconds 6 }
}

try { $browser | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
try { $serve | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "done"
