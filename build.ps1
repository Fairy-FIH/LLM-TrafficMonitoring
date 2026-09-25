# Builds both single-file Windows executables into .\dist
# Usage:  pwsh -File .\build.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$app  = Join-Path $root "src\LlmUsageMonitor.App\LlmUsageMonitor.App.csproj"
$tests = Join-Path $root "tests\LlmUsageMonitor.Tests\LlmUsageMonitor.Tests.csproj"

Write-Host "==> Running tests" -ForegroundColor Cyan
dotnet test $tests -c Release --nologo

Write-Host "==> Cleaning previous output" -ForegroundColor Cyan
Remove-Item -Recurse -Force (Join-Path $root "dist") -ErrorAction SilentlyContinue

Write-Host "==> Publishing self-contained single-file exe" -ForegroundColor Cyan
dotnet publish $app -c Release -r win-x64 -p:SelfContained=true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o (Join-Path $root "dist\self-contained") --nologo

Write-Host "==> Publishing framework-dependent single-file exe" -ForegroundColor Cyan
dotnet publish $app -c Release -r win-x64 -p:SelfContained=false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o (Join-Path $root "dist\framework-dependent") --nologo

Write-Host "==> Done. Output in .\dist" -ForegroundColor Green
Get-ChildItem (Join-Path $root "dist") -Recurse -Filter *.exe |
    Select-Object FullName, @{ N = "MB"; E = { [math]::Round($_.Length / 1MB, 1) } }
