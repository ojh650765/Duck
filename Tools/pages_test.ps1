<#
Serves the Web/ folder exactly the way GitHub Pages will — a plain static file
server with no Content-Encoding header — and photographs the result.

This is the test that matters for a Pages deployment. The build is gzipped, and
a static host cannot tell the browser so; Unity's decompression fallback is
supposed to handle that in JavaScript. "Supposed to" is not evidence, and the
failure mode is a loading bar that never finishes, which nobody notices until a
player opens the link.

    powershell -File Tools\pages_test.ps1 -Seconds 70
#>
param(
    [int]$Seconds = 70,
    [int]$Port = 8199,
    [string]$Root = "C:\Duck\Web",
    [string]$Out = "C:\Duck\Captures\WebGL\pages_test.png"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null

# python -m http.server sends no Content-Encoding, which is precisely the point.
$serve = Start-Process -FilePath "python" `
    -ArgumentList @("-m", "http.server", "$Port", "--directory", $Root) `
    -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 3

$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$userDir = Join-Path $env:TEMP ("pagestest_" + [guid]::NewGuid().ToString("N"))

$browser = Start-Process -FilePath $chrome -PassThru -ArgumentList @(
    "--app=http://127.0.0.1:$Port/index.html",
    "--window-size=1280,720",
    "--window-position=0,0",
    "--user-data-dir=$userDir",
    "--no-first-run",
    "--no-default-browser-check",
    "--autoplay-policy=no-user-gesture-required",
    "--disable-features=CalculateNativeWinOcclusion"
)

Write-Output "serving $Root with no Content-Encoding; waiting $Seconds s..."
Start-Sleep -Seconds $Seconds

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$bmp = New-Object System.Drawing.Bitmap 1280, 720
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size 1280, 720))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

try { $browser | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
try { $serve | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
if ($userDir -like "$env:TEMP\pagestest_*") {
    Remove-Item -LiteralPath $userDir -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output "captured $Out"
