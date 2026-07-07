param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$NoRestore,

    [switch]$SkipNative,

    [switch]$IncludeAnalysisAtlas
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "RayaTrainer.sln"
. (Join-Path $PSScriptRoot "publish-common.ps1")

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

function Assert-RepositoryLayout {
    foreach ($relativePath in @(
        "RayaTrainer.Core",
        "RayaTrainer.Agent",
        "tools/RayaTrainer.BootstrapCompiler"
    )) {
        $path = Join-Path $repoRoot $relativePath
        if (Test-Path -LiteralPath $path) {
            throw "Obsolete or shadow repository path must not exist: $relativePath"
        }
    }
}

Push-Location $repoRoot
try {
    Assert-RepositoryLayout

    if (-not $NoRestore) {
        Invoke-Checked "Restore managed projects" {
            dotnet restore $solution
        }
    }

    Invoke-Checked "Build managed solution" {
        dotnet build $solution -c $Configuration --no-restore --no-incremental --verbosity minimal /m:1 /nr:false
    }

    Invoke-Checked "Verify Direct GameApi generated sources" {
        dotnet run --project "tools/RayaTrainer.ApiGenerator/RayaTrainer.ApiGenerator.csproj" -c $Configuration --no-build -- verify
    }

    Invoke-Checked "Verify registered addresses" {
        dotnet run --project "tools/RayaTrainer.AddressLint/RayaTrainer.AddressLint.csproj" -c $Configuration --no-build
    }

    Invoke-Checked "Run managed tests" {
        dotnet test $solution -c $Configuration --no-restore --no-build --verbosity minimal /m:1 /nr:false
    }

    if ($IncludeAnalysisAtlas) {
        Invoke-Checked "Validate analysis atlas" {
            powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "validate-analysis-index.ps1") -RepoRoot $repoRoot -Rebuild
        }
    }

    if (-not $SkipNative) {
        $msbuild = Resolve-MSBuildPath
        $nativeProjects = @(
            "src/RayaTrainer.Agent/RayaTrainer.Agent.vcxproj",
            "tests/RayaTrainer.Agent.Tests/RayaTrainer.Agent.Tests.vcxproj"
        )

        foreach ($project in $nativeProjects) {
            Invoke-Checked "Build $project" {
                & $msbuild $project /p:Configuration=$Configuration /p:Platform=Win32 /p:ResolveNuGetPackages=false /nologo /v:minimal /m
            }
        }

        Invoke-Checked "Run Agent native tests" {
            & (Join-Path $repoRoot "artifacts/tests/$Configuration/RayaTrainer.Agent.Tests.exe")
        }
    }

    Write-Host "Repository validation passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
