param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$out = if ($SelfContained) { "dist\SpriteLens-win-x64" } else { "dist" }
if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}

if ($SelfContained) {
    dotnet publish SpriteLens.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
} else {
    dotnet publish SpriteLens.csproj -c Release -r win-x64 --self-contained false -o $out
}

Copy-Item README.md $out -Force
Copy-Item docs $out -Recurse -Force
Write-Host ""
Write-Host "Publish complete: $out" -ForegroundColor Green
if (-not $SelfContained) {
    Write-Host "Install the .NET 9 Desktop Runtime before running." -ForegroundColor Yellow
}
