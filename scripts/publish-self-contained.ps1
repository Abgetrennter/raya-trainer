param(
    [string]$OutputRoot,
    [string]$BuildTag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/RayaTrainer.App/RayaTrainer.App.csproj"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts/publish"
}
$output = Join-Path $OutputRoot "RayaTrainer.App-win-x86-self-contained"

. (Join-Path $PSScriptRoot "publish-common.ps1")
$buildVersion = Get-BuildVersionMetadata -RepoRoot $repoRoot -BuildTag $BuildTag
Clear-PublishOutputDirectory -RepoRoot $repoRoot -PublishRoot $OutputRoot -OutputPath $output
Build-AgentDll -RepoRoot $repoRoot

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
    -o $output

Copy-AgentDll -RepoRoot $repoRoot -OutputPath $output
