param (
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Release",
    [string]$OutputFolder = "build"
)

$projectDir = Join-Path $PSScriptRoot "FLARE.UI"
$project = Join-Path $projectDir "FLARE.UI.csproj"
$publishDir = Join-Path $projectDir "bin\$Config\net10.0-windows\$Runtime\publish"
$intermediateDir = Join-Path $projectDir "obj\$Config\net10.0-windows\$Runtime"
$outputDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) {
    $OutputFolder
} else {
    Join-Path $PSScriptRoot $OutputFolder
}

$gitHash = try { git rev-parse --short HEAD 2>$null } catch { "dev" }
if (-not $gitHash) { $gitHash = "dev" }
$gitTag = try { git describe --tags --exact-match 2>$null } catch { "" }

$msbuildProps = @(
    "-p:Configuration=$Config",
    "-p:RuntimeIdentifier=$Runtime",
    "-p:RuntimeIdentifiers=$Runtime",
    "-p:SelfContained=false",
    "-p:DebugSymbols=false",
    "-p:DebugType=None",
    "-p:SourceRevisionId=$gitHash"
)
if ($gitTag -match '^v(.+)$') {
    $msbuildProps += "-p:Version=$($Matches[1])"
    $msbuildProps += "-p:FlareIsRelease=true"
}

Write-Host "Building FLARE executable for $Runtime ($gitHash)..."

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path -LiteralPath $intermediateDir) {
    Remove-Item -LiteralPath $intermediateDir -Recurse -Force
}

dotnet msbuild $project -restore -t:BundleAppOnlyForFrameworkDependent @msbuildProps

if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

$publishedExe = Join-Path $publishDir "FLARE.exe"

if (!(Test-Path -LiteralPath $publishedExe)) {
    throw "Expected published executable was not found: $publishedExe"
}

$startupSmoke = Start-Process -FilePath $publishedExe `
    -ArgumentList @("--smoke-test") `
    -Wait -PassThru -WindowStyle Hidden
if ($startupSmoke.ExitCode -ne 0) {
    throw "Packaged FLARE.exe startup smoke test failed. Expected exit code 0, got $($startupSmoke.ExitCode)."
}

$smokePath = Join-Path ([System.IO.Path]::GetTempPath()) "flare-smoke-outside-root"
$smoke = Start-Process -FilePath $publishedExe `
    -ArgumentList @("--copy-dumps-to", $smokePath) `
    -Wait -PassThru -WindowStyle Hidden
if ($smoke.ExitCode -ne 3) {
    throw "Packaged FLARE.exe smoke test failed. Expected helper refusal exit code 3, got $($smoke.ExitCode)."
}

if (!(Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $outputDir "FLARE.exe") -Force

Write-Host "Done. Output: $(Join-Path $outputDir 'FLARE.exe')"
