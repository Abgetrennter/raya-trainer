param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$utf8Encoding = New-Object Text.UTF8Encoding($false, $true)

function Get-RelativeRepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedRepoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $resolvedPath
    }

    return $resolvedPath.Substring($resolvedRepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

function Get-AtlasDocumentStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $lines = [IO.File]::ReadAllLines($Path, $script:utf8Encoding)
    if ($lines.Count -lt 3 -or $lines[0] -ne '---') {
        return $null
    }

    for ($index = 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -eq '---') {
            break
        }

        if ($lines[$index] -match '^status:\s*[''\"]?([A-Za-z]+)[''\"]?\s*$') {
            return $Matches[1].ToLowerInvariant()
        }
    }

    return $null
}

function Read-RequiredProtocolVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$ContractName
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing $ContractName protocol source: $Path"
    }

    $match = [regex]::Match([IO.File]::ReadAllText($Path), $Pattern)
    if (-not $match.Success) {
        throw "Unable to read $ContractName protocol version from $Path"
    }

    return [int]$match.Groups['version'].Value
}

$managedProtocolPath = Join-Path $resolvedRepoRoot 'src/RayaTrainer.Core/Agent/AgentProtocol.cs'
$nativeProtocolPath = Join-Path $resolvedRepoRoot 'src/RayaTrainer.Agent/AgentProtocol.h'
$managedProtocolVersion = Read-RequiredProtocolVersion `
    -Path $managedProtocolPath `
    -Pattern 'public\s+const\s+ushort\s+Version\s*=\s*(?<version>\d+)\s*;' `
    -ContractName 'managed'
$nativeProtocolVersion = Read-RequiredProtocolVersion `
    -Path $nativeProtocolPath `
    -Pattern 'kAgentProtocolVersion\s*=\s*(?<version>\d+)\s*;' `
    -ContractName 'native'

$violations = @()
if ($managedProtocolVersion -ne $nativeProtocolVersion) {
    $violations += [pscustomobject]@{
        Path = 'src/RayaTrainer.Core/Agent/AgentProtocol.cs'
        Line = 0
        Rule = 'protocol-contract'
        Message = "Managed protocol v$managedProtocolVersion does not match native protocol v$nativeProtocolVersion."
    }
}

$currentProtocolVersion = $managedProtocolVersion
$documents = @()
foreach ($relativePath in @('AGENTS.md', 'CLAUDE.md', '.github/copilot-instructions.md')) {
    $candidate = Join-Path $resolvedRepoRoot $relativePath
    if (Test-Path -LiteralPath $candidate) {
        $documents += (Get-Item -LiteralPath $candidate).FullName
    }
}

$analysisRoot = Join-Path $resolvedRepoRoot 'RA3_Analysis'
if (Test-Path -LiteralPath $analysisRoot) {
    foreach ($document in Get-ChildItem -LiteralPath $analysisRoot -Recurse -File -Filter '*.md') {
        if ((Get-AtlasDocumentStatus -Path $document.FullName) -in @('active', 'partial')) {
            $documents += $document.FullName
        }
    }
}

$obsoleteIdentities = [ordered]@{
    'Ra3Trainer.Core' = 'RayaTrainer.Core'
    'Ra3Trainer.App' = 'RayaTrainer.App'
    'Ra3Trainer.Agent' = 'RayaTrainer.Agent'
    'Ra3Trainer.Tests' = 'RayaTrainer.Tests'
    'Ra3Trainer.ApiGenerator' = 'RayaTrainer.ApiGenerator'
    'Ra3Trainer.AddressLint' = 'RayaTrainer.AddressLint'
    'Ra3Trainer.Smoke' = 'RayaTrainer.Smoke'
    'Ra3Trainer.ContractLint' = 'RayaTrainer.ContractLint'
    'Ra3Trainer.sln' = 'RayaTrainer.sln'
}
$protocolPattern = [regex]'(?i)(?:\u534f\u8bae(?:\u7248\u672c)?\s*(?:\u4e3a\s*)?|protocol(?:\s+version)?(?:\s+is)?\s*)v(?<version>\d+)\b'

foreach ($documentPath in @($documents | Sort-Object -Unique)) {
    $relativePath = Get-RelativeRepoPath -Path $documentPath
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($documentPath, $utf8Encoding)) {
        $lineNumber++

        foreach ($identity in $obsoleteIdentities.GetEnumerator()) {
            if ($line.IndexOf($identity.Key, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $violations += [pscustomobject]@{
                    Path = $relativePath
                    Line = $lineNumber
                    Rule = 'obsolete-identity'
                    Message = "Replace '$($identity.Key)' with '$($identity.Value)'."
                }
            }
        }

        foreach ($match in $protocolPattern.Matches($line)) {
            $documentProtocolVersion = [int]$match.Groups['version'].Value
            if ($documentProtocolVersion -ne $currentProtocolVersion) {
                $violations += [pscustomobject]@{
                    Path = $relativePath
                    Line = $lineNumber
                    Rule = 'protocol-version'
                    Message = "Active documentation references protocol v$documentProtocolVersion; current source contract is v$currentProtocolVersion."
                }
            }
        }
    }
}

$violations = @($violations | Sort-Object Path, Line, Rule, Message)
if ($violations.Count -gt 0) {
    foreach ($violation in $violations) {
        Write-Host ("{0}:{1}: [{2}] {3}" -f $violation.Path, $violation.Line, $violation.Rule, $violation.Message) -ForegroundColor Red
    }

    throw "Active documentation drift detected: $($violations.Count) violation(s)."
}

Write-Host ("Active documentation drift lint passed. Protocol: v{0}; scoped documents: {1}." -f $currentProtocolVersion, @($documents | Sort-Object -Unique).Count) -ForegroundColor Green
