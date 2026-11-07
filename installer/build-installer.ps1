param(
    [string]$Version = "1.0.0",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $here
$payload = Join-Path $here "payload"
$assets = Join-Path $here "assets"
$output = Join-Path $repo "dist\installer"
$iconPath = Join-Path $repo "Assets\SpriteLens.png"

function Find-ISCC {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    throw "ISCC.exe not found. Install Inno Setup 6 first: winget install JRSoftware.InnoSetup"
}

function New-WizardImages {
    param(
        [string]$IconPath,
        [string]$Destination,
        [string]$Version
    )

    Add-Type -AssemblyName System.Drawing
    $icon = [Drawing.Image]::FromFile($IconPath)
    try {
        $large = New-Object Drawing.Bitmap 164, 314
        $graphics = [Drawing.Graphics]::FromImage($large)
        try {
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit

            $rect = New-Object Drawing.Rectangle 0, 0, 164, 314
            $background = New-Object Drawing.Drawing2D.LinearGradientBrush $rect,
                ([Drawing.Color]::FromArgb(255, 18, 22, 34)),
                ([Drawing.Color]::FromArgb(255, 8, 10, 16)), 90.0
            $graphics.FillRectangle($background, $rect)
            $background.Dispose()

            $glow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(28, 124, 108, 255))
            $graphics.FillEllipse($glow, 12, 10, 140, 140)
            $glow.Dispose()

            $graphics.DrawImage($icon, 34, 38, 96, 96)

            $white = [Drawing.Brushes]::White
            $accent = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 148, 132, 255))
            $muted = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 143, 153, 175))
            $titleFont = New-Object Drawing.Font "Microsoft YaHei", 19.0, ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
            $subFont = New-Object Drawing.Font "Segoe UI", 12.0, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
            $smallFont = New-Object Drawing.Font "Microsoft YaHei", 10.0, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)

            $format = New-Object Drawing.StringFormat
            $format.Alignment = [Drawing.StringAlignment]::Center
            $format.LineAlignment = [Drawing.StringAlignment]::Center

            $titleRect = New-Object Drawing.RectangleF 0, 158, 164, 32
            $subRect = New-Object Drawing.RectangleF 0, 194, 164, 22
            $hintRect = New-Object Drawing.RectangleF 0, 222, 164, 20
            $versionRect = New-Object Drawing.RectangleF 0, 270, 164, 20
            $graphics.DrawString("精灵镜", $titleFont, $white, $titleRect, $format)
            $graphics.DrawString("SpriteLens", $subFont, $accent, $subRect, $format)
            $graphics.DrawString("精灵图 / 图集查看器", $smallFont, $muted, $hintRect, $format)

            $linePen = New-Object Drawing.Pen $accent, 1.0
            $graphics.DrawLine($linePen, 42, 258, 122, 258)
            $linePen.Dispose()
            $graphics.DrawString("v$Version", $smallFont, $muted, $versionRect, $format)
        }
        finally {
            $graphics.Dispose()
        }
        $large.Save((Join-Path $Destination "wizard.bmp"), [Drawing.Imaging.ImageFormat]::Bmp)
        $large.Dispose()

        $small = New-Object Drawing.Bitmap 55, 55
        $smallGraphics = [Drawing.Graphics]::FromImage($small)
        try {
            $smallGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $smallGraphics.Clear([Drawing.Color]::FromArgb(255, 12, 15, 24))
            $dot = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 24, 28, 44))
            $smallGraphics.FillEllipse($dot, 0, 0, 55, 55)
            $dot.Dispose()
            $smallGraphics.DrawImage($icon, 8, 8, 39, 39)
        }
        finally {
            $smallGraphics.Dispose()
        }
        $small.Save((Join-Path $Destination "wizard-small.bmp"), [Drawing.Imaging.ImageFormat]::Bmp)
        $small.Dispose()

        $titleFont.Dispose()
        $subFont.Dispose()
        $smallFont.Dispose()
        $accent.Dispose()
        $muted.Dispose()
        $format.Dispose()
    }
    finally {
        $icon.Dispose()
    }
}

if (-not $SkipPublish) {
    if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $payload | Out-Null

    Write-Host "Publishing self-contained payload to $payload ..."
    & dotnet publish (Join-Path $repo "SpriteLens.csproj") -c Release -r win-x64 `
        --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $payload
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

$exe = Join-Path $payload "SpriteLens.exe"
if (-not (Test-Path $exe)) {
    throw "Payload is missing SpriteLens.exe. Run without -SkipPublish first."
}

New-Item -ItemType Directory -Force -Path $assets | Out-Null
New-Item -ItemType Directory -Force -Path $output | Out-Null
New-WizardImages -IconPath $iconPath -Destination $assets -Version $Version

$iscc = Find-ISCC
Write-Host "Compiling installer with $iscc ..."
& $iscc "/DAppVersion=$Version" (Join-Path $here "SpriteLens.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE"
}

$setup = Join-Path $output "SpriteLens-Setup-$Version-x64.exe"
if (-not (Test-Path $setup)) {
    throw "Installer output not found: $setup"
}

$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
$hashFile = Join-Path $output "SpriteLens-Setup-$Version-x64.sha256"
Set-Content -Path $hashFile -Value "$hash  $(Split-Path $setup -Leaf)" -Encoding ASCII

$sizeMb = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "Installer ready: $setup" -ForegroundColor Green
Write-Host "Size: $sizeMb MB"
Write-Host "SHA256: $hash"
