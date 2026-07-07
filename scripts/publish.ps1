<#
.SYNOPSIS
    Builds and packages RA3 Trainer for release.

.DESCRIPTION
    This script builds both framework-dependent and self-contained versions,
    creates release zip packages, and validates them.

.PARAMETER OutputRoot
    Root directory for publish output. Defaults to artifacts/publish.

.PARAMETER SkipTests
    Skip running tests before publishing.

.PARAMETER SkipValidation
    Skip release package validation.

.EXAMPLE
    ./scripts/publish.ps1
    ./scripts/publish.ps1 -OutputRoot "C:/temp/release"
    ./scripts/publish.ps1 -SkipTests
#>
param(
    [string]$OutputRoot,
    [string]$BuildTag,
    [switch]$SkipTests,
    [switch]$SkipValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/RayaTrainer.App/RayaTrainer.App.csproj"
$solution = Join-Path $repoRoot "RayaTrainer.sln"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts/publish"
}

. (Join-Path $PSScriptRoot "publish-common.ps1")
$buildVersion = Get-BuildVersionMetadata -RepoRoot $repoRoot -BuildTag $BuildTag
$version = $buildVersion.PackageVersion
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RA3 Trainer Release Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $($buildVersion.InformationalVersion)" -ForegroundColor Yellow
Write-Host "Output:  $OutputRoot" -ForegroundColor Yellow
Write-Host ""

# Step 1: Run tests
if (-not $SkipTests) {
    Write-Host "[1/5] Running tests..." -ForegroundColor Green
    dotnet test $solution -c Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed. Aborting publish."
    }
    Write-Host "      Tests passed." -ForegroundColor Gray
} else {
    Write-Host "[1/5] Skipping tests (requested)" -ForegroundColor Yellow
}

# Step 2: Build Agent DLL
Write-Host "[2/5] Building Agent DLL (x86)..." -ForegroundColor Green
Build-AgentDll -RepoRoot $repoRoot
Write-Host "      Agent DLL built." -ForegroundColor Gray

# Step 3: Publish framework-dependent
Write-Host "[3/5] Publishing framework-dependent build..." -ForegroundColor Green
$fdOutput = Join-Path $OutputRoot "RayaTrainer.App-win-x86-framework-dependent"
Clear-PublishOutputDirectory -RepoRoot $repoRoot -PublishRoot $OutputRoot -OutputPath $fdOutput

dotnet publish $project `
    -c Release `
    -r win-x86 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishSelfContained=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$($buildVersion.PackageVersion) `
    -p:AssemblyVersion=$($buildVersion.NumericVersion) `
    -p:FileVersion=$($buildVersion.NumericVersion) `
    -p:InformationalVersion=$($buildVersion.InformationalVersion) `
    -o $fdOutput

Copy-AgentDll -RepoRoot $repoRoot -OutputPath $fdOutput
Copy-RuntimeCheckScripts -RepoRoot $repoRoot -OutputPath $fdOutput
$fdSize = (Get-ChildItem -Path $fdOutput -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "      Framework-dependent: $([math]::Round($fdSize, 1)) MB" -ForegroundColor Gray

# Step 4: Publish self-contained
Write-Host "[4/5] Publishing self-contained build..." -ForegroundColor Green
$scOutput = Join-Path $OutputRoot "RayaTrainer.App-win-x86-self-contained"
Clear-PublishOutputDirectory -RepoRoot $repoRoot -PublishRoot $OutputRoot -OutputPath $scOutput

dotnet publish $project `
    -c Release `
    -r win-x86 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishSelfContained=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$($buildVersion.PackageVersion) `
    -p:AssemblyVersion=$($buildVersion.NumericVersion) `
    -p:FileVersion=$($buildVersion.NumericVersion) `
    -p:InformationalVersion=$($buildVersion.InformationalVersion) `
    -o $scOutput

Copy-AgentDll -RepoRoot $repoRoot -OutputPath $scOutput
$scSize = (Get-ChildItem -Path $scOutput -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "      Self-contained: $([math]::Round($scSize, 1)) MB" -ForegroundColor Gray

# Step 5: Create release zips
Write-Host "[5/5] Creating release packages..." -ForegroundColor Green
$releaseDir = Join-Path $OutputRoot "release"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$fdZip = Join-Path $releaseDir "RayaTrainer-v$version-win-x86-framework-dependent.zip"
$scZip = Join-Path $releaseDir "RayaTrainer-v$version-win-x86-self-contained.zip"

Compress-Archive -Path (Join-Path $fdOutput "*") -DestinationPath $fdZip -Force
Compress-Archive -Path (Join-Path $scOutput "*") -DestinationPath $scZip -Force

$fdZipSize = [math]::Round((Get-Item $fdZip).Length / 1MB, 1)
$scZipSize = [math]::Round((Get-Item $scZip).Length / 1MB, 1)
Write-Host "      Created: $(Split-Path $fdZip -Leaf) ($fdZipSize MB)" -ForegroundColor Gray
Write-Host "      Created: $(Split-Path $scZip -Leaf) ($scZipSize MB)" -ForegroundColor Gray

# Step 6: Validate
if (-not $SkipValidation) {
    Write-Host ""
    Write-Host "Validating release packages..." -ForegroundColor Green
    & (Join-Path $PSScriptRoot "validate-release-package.ps1") -ZipPath @($fdZip, $scZip)
    Write-Host "      Validation passed." -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "Skipping validation (requested)" -ForegroundColor Yellow
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Release Build Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $version" -ForegroundColor Yellow
Write-Host ""
Write-Host "Packages:" -ForegroundColor White
Write-Host "  $fdZip" -ForegroundColor Gray
Write-Host "  $scZip" -ForegroundColor Gray
Write-Host ""
Write-Host "To create a GitHub release:" -ForegroundColor White
Write-Host "  git tag v$version" -ForegroundColor Gray
Write-Host "  git push origin v$version" -ForegroundColor Gray
Write-Host ""
