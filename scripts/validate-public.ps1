param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$NoRestore,

    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$resolvedRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$solution = Join-Path $resolvedRoot "RayaTrainer.Public.sln"
$testProject = Join-Path $resolvedRoot "tests/RayaTrainer.Public.Tests/RayaTrainer.Public.Tests.csproj"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Missing public solution: $solution"
}
if (-not (Test-Path -LiteralPath $testProject)) {
    throw "Missing public test project: $testProject"
}

$solutionSource = Get-Content -Raw -LiteralPath $solution
foreach ($blockedProject in @(
    "RayaTrainer.Agent",
    "RayaTrainer.ApiGenerator",
    "RayaTrainer.AddressLint",
    "RayaTrainer.Smoke",
    "RayaTrainer.ContractLint"
)) {
    if ($solutionSource.Contains($blockedProject, [StringComparison]::Ordinal)) {
        throw "Public solution references private project: $blockedProject"
    }
}

$projectionReceipt = Join-Path $resolvedRoot ".public-source.json"
if (Test-Path -LiteralPath $projectionReceipt) {
    foreach ($privatePath in @(
        "RayaTrainer.sln",
        "src/RayaTrainer.Agent",
        "tests/RayaTrainer.Tests",
        "tests/RayaTrainer.Agent.Tests",
        "tools/RayaTrainer.ApiGenerator",
        "tools/RayaTrainer.AddressLint",
        "tools/RayaTrainer.Smoke",
        "tools/RayaTrainer.ContractLint"
    )) {
        $candidate = Join-Path $resolvedRoot $privatePath
        if (Test-Path -LiteralPath $candidate) {
            throw "Projected tree contains private implementation path: $privatePath"
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $resolvedRoot "docs/SECURITY.md"))) {
        throw "Projected tree is missing docs/SECURITY.md."
    }
}

Push-Location -LiteralPath $resolvedRoot
try {
    if (-not $NoRestore) {
        Invoke-Checked "Restore public solution" {
            dotnet restore $solution
        }
    }

    Invoke-Checked "Build public solution" {
        dotnet build $solution -c $Configuration --no-restore --no-incremental --verbosity minimal /m:1 /nr:false
    }

    Invoke-Checked "Run public tests" {
        dotnet test $testProject -c $Configuration --no-restore --no-build --verbosity minimal /m:1 /nr:false
    }

    Write-Host "Public solution validation passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
