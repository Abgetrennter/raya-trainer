<#
.SYNOPSIS
    Public repo leak pre-flight check.
.DESCRIPTION
    Checks source tree, binary packages, assembly namespaces, and dependency
    legal files for content that must not enter the public projection.
.PARAMETER CheckLevel
    source | binary | namespace | dependency | all (default: all)
.PARAMETER TargetDir
    Directory to scan (default: repo root)
.PARAMETER FrameworkDependentZip
    Path to framework-dependent ZIP (binary mode only)
.PARAMETER SelfContainedZip
    Path to self-contained ZIP (binary mode only)
.PARAMETER FailFast
    Exit on first violation
.EXAMPLE
    pwsh -File scripts/preflight-leak-check.ps1 -CheckLevel source
    pwsh -File scripts/preflight-leak-check.ps1 -CheckLevel binary -FrameworkDependentZip a.zip -SelfContainedZip b.zip
#>
[CmdletBinding()]
param(
    [ValidateSet('source','binary','namespace','dependency','all')]
    [string]$CheckLevel = 'all',
    [string]$TargetDir = (Resolve-Path "$PSScriptRoot/.."),
    [string]$FrameworkDependentZip,
    [string]$SelfContainedZip,
    [switch]$FailFast
)

$exitCode = 0
$ErrorActionPreference = 'Continue'

# ─── Layer 1: Source tree scan ──────────────────────────────────────────
function Test-SourceTree {
    $violations = @()
    $forbiddenPaths = @(
        'RA3_Analysis', 'vendor/Red Alert 3', 'vendor/CameraBrigdeRelease',
        'vendor/RA3_Engine_Reference',
        'tools/corona', 'tools/diag', 'tools/Ra3LuaConsole',
        'tools/Ra3Trainer.ModProtocolScanner', 'tools/RayaTrainer.ModProtocolScanner',
        'tools/CommentStripper',
        '.agents', '.claude', '.codex', '.cortexkit', '.sisyphus', '.spec-workflow',
        'docs/archive', 'docs/superpowers/archive', 'docs/superpowers/plans',
        'docs/superpowers/specs', 'docs/import-tables', 'docs/release-notes',
        'docs/asset-approvals.md', 'docs/private',
        'scripts/migrate-to-public.ps1', 'scripts/migrate-allowlist.txt',
        'scripts/migrate-allowlist-excluded.txt',
        'tests/RayaTrainer.Tests/RepositoryValidationScriptTests.cs'
    )
    $forbiddenFiles = @('*.ct', '*.id0', '*.id1', '*.id2', '*.nam', '*.til', '*.i64')
    $forbiddenContent = @(
        'Cheat Engine', 'Script Only Work For Cheat Engine',
        'PlayerTech_Celestial', 'WuRa3GameDebug', 'EA.Blackbox',
        'Internal use only', 'DONOTPUBLISH'
    )
    $forbiddenExactNames = @('code.txt')

    foreach ($fp in $forbiddenPaths) {
        $full = Join-Path $TargetDir $fp
        if (Test-Path -LiteralPath $full) { $violations += "forbidden path: $fp" }
    }
    foreach ($pattern in $forbiddenFiles) {
        $found = Get-ChildItem $TargetDir -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
        foreach ($f in $found) { $violations += "forbidden file: $($f.FullName.Replace($TargetDir,''))" }
    }
    $skipPaths = @('\bin\', '\obj\', '\vendor\', '\Generated\')
    $skipSelf = 'scripts\preflight-leak-check.ps1'
    function ShouldSkip($rel) {
        foreach ($sp in $script:skipPaths) { if ($rel -match [regex]::Escape($sp)) { return $true } }
        if ($rel -match [regex]::Escape($script:skipSelf)) { return $true }
        return $false
    }

    foreach ($name in $forbiddenExactNames) {
        Get-ChildItem $TargetDir -Recurse -File | Where-Object { $_.Name -eq $name } | ForEach-Object {
            $rel = $_.FullName.Replace($TargetDir, '')
            if (-not (ShouldSkip $rel)) { $violations += "forbidden file: $rel" }
        }
    }
    $textFiles = Get-ChildItem $TargetDir -Recurse -File -Include *.cs,*.h,*.cpp,*.asm,*.xaml,*.ps1,*.json,*.md -ErrorAction SilentlyContinue | Where-Object {
        $rel = $_.FullName.Replace($TargetDir, '')
        -not (ShouldSkip $rel)
    }
    foreach ($pattern in $forbiddenContent) {
        if ($textFiles -and $textFiles.FullName) {
            $matches = Select-String -LiteralPath $textFiles.FullName -Pattern $pattern -SimpleMatch -ErrorAction SilentlyContinue
            foreach ($m in $matches) { $violations += "forbidden content '$pattern': $($m.Path.Replace($TargetDir,'')):line$($m.LineNumber)" }
        }
    }
    return $violations
}

# ─── Layer 2: Binary package scan ───────────────────────────────────────
function Test-Binary {
    param([string]$ZipA, [string]$ZipB)
    $violations = @()
    $whitelist = @(
        'RayaTrainer.App.exe','RayaTrainer.App.dll','RayaTrainer.App.pdb',
        'RayaTrainer.App.runtimeconfig.json','RayaTrainer.App.deps.json',
        'RayaTrainer.Core.dll','RayaTrainer.Core.pdb',
        'RayaTrainer.Agent.dll','RayaTrainer.Agent.pdb',
        'RayaTrainer.settings.json',
        'check-dotnet-runtime.ps1',
        'pack.json','secret-protocols.txt','reinforcements.txt'
    )
    $forbidden = @('Ra3Trainer.settings.json','Ra3Trainer.Agent.dll','code.txt')
    $bannedStrings = @('Cheat Engine','WuRa3GameDebug','EA.Blackbox','Internal use only','DONOTPUBLISH')

    foreach ($zip in @($ZipA, $ZipB)) {
        if (-not $zip -or -not (Test-Path -LiteralPath $zip)) {
            $violations += "ZIP missing: $zip"
            continue
        }
        $tempExtract = Join-Path $env:TEMP ("preflight-binary-" + (Get-Random))
        try {
            Expand-Archive -Path $zip -DestinationPath $tempExtract -Force
            $files = Get-ChildItem -Recurse -File -LiteralPath $tempExtract
            foreach ($f in $files) {
                $rel = $f.FullName.Substring($tempExtract.Length).TrimStart('\','/')
                foreach ($bad in $forbidden) {
                    if ($rel -like "*$bad*") { $violations += "forbidden entry '$rel' in $zip" }
                }
                $ext = $f.Extension.ToLowerInvariant()
                if ($ext -in '.json','.txt','.xml','.config','.ps1','.bat','.cmd','.md') {
                    $content = Get-Content -Raw -LiteralPath $f.FullName -ErrorAction SilentlyContinue
                    if ($content) {
                        foreach ($bs in $bannedStrings) {
                            if ($content -match [regex]::Escape($bs)) {
                                $violations += "banned string '$bs' in $zip!$rel"
                            }
                        }
                    }
                }
            }
            $agent = Get-ChildItem -Recurse -File -LiteralPath $tempExtract -Filter 'RayaTrainer.Agent.dll' | Select-Object -First 1
            if (-not $agent) {
                $violations += "RayaTrainer.Agent.dll missing in $zip"
            } else {
                $bytes = [byte[]]::new(4096)
                $stream = [System.IO.File]::OpenRead($agent.FullName)
                try { $null = $stream.Read($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
                $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
                if ($peOffset + 6 -ge $bytes.Length) {
                    $violations += "Agent DLL too small for PE header in $zip"
                } else {
                    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
                    if ($machine -ne 0x014c) {
                        $violations += "Agent DLL is not x86 PE (machine=0x$($machine.ToString('X4'))) in $zip"
                    }
                }
            }
            $packJson = Get-ChildItem -Recurse -File -LiteralPath $tempExtract -Filter 'pack.json' | Where-Object { $_.FullName -like '*Corona*' } | Select-Object -First 1
            if ($packJson) {
                $manifest = Get-Content -Raw -LiteralPath $packJson.FullName | ConvertFrom-Json
                $packDir = $packJson.DirectoryName
                foreach ($asset in $manifest.assets) {
                    $assetPath = Join-Path $packDir $asset.path
                    if (-not (Test-Path -LiteralPath $assetPath)) {
                        $violations += "asset pack missing file $($asset.path) in $zip"
                        continue
                    }
                    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $assetPath).Hash.ToLower()
                    if ($actualHash -ne $asset.sha256.ToLower()) {
                        $violations += "asset pack hash mismatch $($asset.path) in $zip"
                    }
                }
            }
        } finally {
            Remove-Item -Recurse -Force -LiteralPath $tempExtract -ErrorAction SilentlyContinue
        }
    }
    return $violations
}

# ─── Layer 3: Namespace scan (reflection-based) ─────────────────────────
function Test-Namespace {
    $violations = @()
    $asmPaths = Get-ChildItem -Recurse -File -LiteralPath $TargetDir -Filter 'RayaTrainer.*.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -or $_.FullName -match '\\Release\\' }
    if (-not $asmPaths) {
        $asmPaths = Get-ChildItem -Recurse -File -LiteralPath $TargetDir -Filter 'RayaTrainer.*.dll' -ErrorAction SilentlyContinue
    }
    if (-not $asmPaths) {
        $violations += 'no RayaTrainer.*.dll found for namespace check - build first'
        return $violations
    }
    foreach ($asm in $asmPaths) {
        Write-Host "  reflecting $($asm.Name)"
        $loaded = [System.Reflection.Assembly]::LoadFrom($asm.FullName)
        $types = $loaded.GetTypes()
        foreach ($t in $types) {
            if ($t.Namespace -and $t.Namespace.StartsWith('Ra3Trainer.', [StringComparison]::Ordinal)) {
                $violations += "$($asm.Name): type '$($t.FullName)' in legacy namespace '$($t.Namespace)'"
            }
        }
    }
    return $violations
}

# ─── Layer 4: Dependency / legal file scan ──────────────────────────────
function Test-Dependency {
    $violations = @()
    foreach ($f in @('LICENSE','NOTICE','THIRD-PARTY-NOTICES.txt')) {
        $p = Join-Path $TargetDir $f
        if (-not (Test-Path -LiteralPath $p)) {
            $violations += "missing $f at repo root"
        } elseif ((Get-Item -LiteralPath $p).Length -lt 100) {
            $violations += "$f looks truncated (<100 bytes)"
        }
    }
    $vendoredExpected = @(
        'src/RayaTrainer.Agent/vendor/zydis/LICENSE',
        'src/RayaTrainer.Agent/vendor/zycore/LICENSE'
    )
    foreach ($v in $vendoredExpected) {
        $p = Join-Path $TargetDir $v
        if (-not (Test-Path -LiteralPath $p)) {
            $violations += "missing vendored license: $v"
        }
    }
    $tpn = Get-Content -Raw -LiteralPath (Join-Path $TargetDir 'THIRD-PARTY-NOTICES.txt') -ErrorAction SilentlyContinue
    if (-not $tpn) {
        $violations += 'THIRD-PARTY-NOTICES.txt not readable'
    } else {
        foreach ($kw in @('Iced','QRCoder','Zydis','Zycore','zasm','.NET')) {
            if ($tpn -notmatch [regex]::Escape($kw)) {
                $violations += "THIRD-PARTY-NOTICES.txt missing entry for '$kw'"
            }
        }
    }
    return $violations
}

# ─── Mode dispatch ──────────────────────────────────────────────────────
switch ($CheckLevel) {
    'source'    { $violations = Test-SourceTree }
    'binary'    {
        if (-not $FrameworkDependentZip -or -not $SelfContainedZip) {
            Write-Host "::error::binary mode requires -FrameworkDependentZip and -SelfContainedZip"
            exit 1
        }
        $violations = Test-Binary -ZipA $FrameworkDependentZip -ZipB $SelfContainedZip
    }
    'namespace' { $violations = Test-Namespace }
    'dependency' { $violations = Test-Dependency }
    'all' {
        Write-Host '=== source ==='
        $v1 = Test-SourceTree
        Write-Host '=== namespace ==='
        $v3 = Test-Namespace
        Write-Host '=== dependency ==='
        $v4 = Test-Dependency
        $violations = $v1 + $v3 + $v4
    }
}

if ($violations.Count -gt 0) {
    Write-Host "::error::preflight FAILED ($($violations.Count) violations)"
    $violations | ForEach-Object { Write-Host "  - $_" }
    $exitCode = 1
} else {
    Write-Host "preflight PASSED"
}

exit $exitCode
