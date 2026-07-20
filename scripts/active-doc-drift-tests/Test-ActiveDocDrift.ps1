Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'validate-active-doc-drift.ps1'
$powershell = (Get-Command powershell -ErrorAction Stop).Source
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoots = @()

function Write-FixtureFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $path = Join-Path $Root $RelativePath
    $directory = Split-Path -Parent $path
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText($path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function New-FixtureRoot {
    $root = Join-Path $tempBase ('raya-active-doc-drift-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $script:fixtureRoots += $root

    Write-FixtureFile -Root $root -RelativePath 'src/RayaTrainer.Core/Agent/AgentProtocol.cs' -Content 'public static class AgentProtocol { public const ushort Version = 9; }'
    Write-FixtureFile -Root $root -RelativePath 'src/RayaTrainer.Core/Agent/apis.json' -Content '{"apis":[]}'
    Write-FixtureFile -Root $root -RelativePath 'src/RayaTrainer.Core/Agent/AgentBuildIdentity.cs' -Content 'public static class AgentBuildIdentity { public const ulong Fingerprint = 0x5241594100090001UL; }'
    Write-FixtureFile -Root $root -RelativePath 'src/RayaTrainer.Agent/AgentProtocol.h' -Content 'inline constexpr uint16_t kAgentProtocolVersion = 9;'
    return $root
}

function Invoke-LintFixture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $powershell -NoProfile -ExecutionPolicy Bypass -File $lintScript -RepoRoot $Root 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine)
    }
}

try {
    $validRoot = New-FixtureRoot
    Write-FixtureFile -Root $validRoot -RelativePath 'AGENTS.md' -Content 'Current source: src/RayaTrainer.Core; current protocol is v9.'
    Write-FixtureFile -Root $validRoot -RelativePath 'RA3_Analysis/topic/active.md' -Content "---`nstatus: active`n---`nCurrent protocol v9."
    Write-FixtureFile -Root $validRoot -RelativePath 'RA3_Analysis/topic/historical.md' -Content "---`nstatus: archived`n---`nHistorical protocol v7; src/Ra3Trainer.Core."
    $validResult = Invoke-LintFixture -Root $validRoot
    if ($validResult.ExitCode -ne 0) {
        throw "Expected valid fixture to pass. Output:`n$($validResult.Output)"
    }

    $identityRoot = New-FixtureRoot
    Write-FixtureFile -Root $identityRoot -RelativePath 'RA3_Analysis/topic/active.md' -Content "---`nstatus: active`n---`nPath: src/Ra3Trainer.Core/Agent/apis.json"
    $identityResult = Invoke-LintFixture -Root $identityRoot
    if ($identityResult.ExitCode -eq 0 -or $identityResult.Output -notmatch 'obsolete-identity') {
        throw "Expected obsolete identity fixture to fail deterministically. Output:`n$($identityResult.Output)"
    }

    $protocolRoot = New-FixtureRoot
    $protocolWord = -join @([char]0x534F, [char]0x8BAE)
    $versionWord = -join @([char]0x7248, [char]0x672C)
    $isWord = [char]0x4E3A
    Write-FixtureFile -Root $protocolRoot -RelativePath 'AGENTS.md' -Content "${protocolWord}${versionWord}${isWord} v7."
    $protocolResult = Invoke-LintFixture -Root $protocolRoot
    if ($protocolResult.ExitCode -eq 0 -or $protocolResult.Output -notmatch 'protocol-version') {
        throw "Expected stale protocol fixture to fail deterministically. Output:`n$($protocolResult.Output)"
    }

    Write-Host 'PASS: active documentation drift lint fixtures' -ForegroundColor Green
}
finally {
    foreach ($root in $fixtureRoots) {
        $resolvedRoot = [IO.Path]::GetFullPath($root)
        if (-not $resolvedRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove fixture outside the temp directory: $resolvedRoot"
        }

        if (Test-Path -LiteralPath $resolvedRoot) {
            Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
        }
    }
}
