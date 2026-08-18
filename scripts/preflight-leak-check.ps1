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
.PARAMETER WebMiniFrameworkDependentZip
    Optional path to the WebMini framework-dependent ZIP (binary mode only)
.PARAMETER WebMiniSelfContainedZip
    Optional path to the WebMini self-contained ZIP (binary mode only)
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
    [string]$WebMiniFrameworkDependentZip,
    [string]$WebMiniSelfContainedZip,
    [switch]$FailFast
)

$exitCode = 0
$ErrorActionPreference = 'Continue'

# ─── Layer 1: Source tree scan ──────────────────────────────────────────
function Test-SourceTree {
    $violations = @()
    $forbiddenPaths = @(
        '.gitmodules', 'RA3_Analysis', 'vendor/Red Alert 3', 'vendor/CameraBrigdeRelease',
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
        'Internal use only', 'DONOTPUBLISH', 'RA3-Engine-Atlas'
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
    param([string[]]$Zips)
    $violations = @()

    # Structural validation (Agent DLL PE/x86/DLL characteristics + blocked native
    # deps, forbidden entries incl. legacy names and code.txt, local path leaks,
    # Agent presence) lives in a single source of truth: validate-release-package.ps1.
    $structural = Join-Path $PSScriptRoot 'validate-release-package.ps1'
    $zips = @($Zips | Where-Object { $_ -and (Test-Path -LiteralPath $_) })
    foreach ($zip in $Zips) {
        if (-not $zip -or -not (Test-Path -LiteralPath $zip)) {
            $violations += "ZIP missing: $zip"
        }
    }
    if ($zips.Count -gt 0) {
        if (Test-Path -LiteralPath $structural) {
            # Invoke via -Command (not -File) so the -ZipPath array binds
            # correctly to the string[] parameter; a nested -File call splits the
            # array across positional slots.
            $zipLiterals = ($zips | ForEach-Object { "    '$_'" }) -join "`n"
            $scriptBlock = @"
& '$structural' -ZipPath @(
$zipLiterals
)
"@
            $structuralOutput = & pwsh -NoProfile -Command $scriptBlock 2>&1
            $structuralExit = $LASTEXITCODE
            foreach ($line in $structuralOutput) { Write-Host $line }
            if ($structuralExit -ne 0) {
                $violations += 'structural release-package validation failed (validate-release-package.ps1)'
            }
        } else {
            $violations += "structural validator not found: $structural"
        }
    }

    # Preflight-only checks not covered by validate-release-package.ps1:
    # banned internal/marketing strings and Corona asset-pack hash integrity.
    $bannedStrings = @('Cheat Engine','WuRa3GameDebug','EA.Blackbox','Internal use only','DONOTPUBLISH')
    foreach ($zip in $zips) {
        $tempExtract = Join-Path $env:TEMP ("preflight-binary-" + (Get-Random))
        try {
            Expand-Archive -Path $zip -DestinationPath $tempExtract -Force
            $files = Get-ChildItem -Recurse -File -LiteralPath $tempExtract
            foreach ($f in $files) {
                $rel = $f.FullName.Substring($tempExtract.Length).TrimStart('\','/')
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

# ─── Layer 3: Namespace scan (PE metadata based) ────────────────────────
function Test-Namespace {
    $violations = @()
    $asmPaths = Get-ChildItem -Recurse -File -LiteralPath $TargetDir -Filter 'RayaTrainer.*.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(\.git|\.worktrees)\\' }
    if (-not $asmPaths) {
        $violations += 'no RayaTrainer.*.dll found for namespace check - build first'
        return $violations
    }
    foreach ($asm in $asmPaths) {
        $stream = $null
        $peReader = $null
        try {
            $stream = [System.IO.File]::OpenRead($asm.FullName)
            $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
            if (-not $peReader.HasMetadata) {
                Write-Host "  skipping native PE $($asm.Name)"
                continue
            }

            Write-Host "  scanning metadata $($asm.Name)"
            $metadataReader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
            foreach ($handle in $metadataReader.TypeDefinitions) {
                $definition = $metadataReader.GetTypeDefinition($handle)
                $namespace = $metadataReader.GetString($definition.Namespace)
                if ($namespace -and $namespace.StartsWith('Ra3Trainer.', [StringComparison]::Ordinal)) {
                    $name = $metadataReader.GetString($definition.Name)
                    $violations += "$($asm.Name): type '$namespace.$name' in legacy namespace '$namespace'"
                }
            }
        } catch {
            $violations += "$($asm.Name): namespace metadata scan failed: $($_.Exception.Message)"
        } finally {
            if ($peReader) { $peReader.Dispose() }
            if ($stream) { $stream.Dispose() }
        }
    }
    return @($violations | Sort-Object -Unique)
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
        'src/RayaTrainer.Agent/vendor/zycore/LICENSE',
        'src/RayaTrainer.Agent/vendor/imgui/LICENSE.txt',
        'src/RayaTrainer.Agent/vendor/minhook/LICENSE.txt'
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
        foreach ($kw in @('Iced','QRCoder','Zydis','Zycore','Dear ImGui','MinHook','zasm','.NET')) {
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
        $binaryZips = @($FrameworkDependentZip, $SelfContainedZip)
        if ($WebMiniFrameworkDependentZip) { $binaryZips += $WebMiniFrameworkDependentZip }
        if ($WebMiniSelfContainedZip) { $binaryZips += $WebMiniSelfContainedZip }
        $violations = Test-Binary -Zips $binaryZips
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
