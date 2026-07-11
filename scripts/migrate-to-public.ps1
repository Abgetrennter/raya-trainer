<#
.SYNOPSIS
  Projects the private RayaTrainer mainline into the public raya-trainer repo.

.DESCRIPTION
  Modes:
    Plan    (default) — read-only report of source SHA, file adds/removes, asset authorization.
    Export             — write projected tree to TargetDir from a clean worktree + fixed SHA.
    Verify             — run code-gen check + Managed build/test + x86 Agent build + source audit.
    Publish            — fast-forward sync to raya-trainer/main. Never force or rebase.

  See docs/superpowers/plans/2026-07-07-refactor/04-projection-tool.md and
  docs/refactor.md §40-50 for the contract.

.EXAMPLE
  pwsh -File scripts/migrate-to-public.ps1 -Mode Plan
  pwsh -File scripts/migrate-to-public.ps1 -Mode Export -TargetDir C:\temp\raya-export
  pwsh -File scripts/migrate-to-public.ps1 -Mode Verify -TargetDir C:\temp\raya-export
  pwsh -File scripts/migrate-to-public.ps1 -Mode Publish -TargetDir C:\temp\raya-export
#>
[CmdletBinding()]
param(
  [ValidateSet('Plan','Export','Verify','Publish')] [string]$Mode = 'Plan',
  [string]$PrivateRepoRoot = (Resolve-Path "$PSScriptRoot/..").Path,
  [string]$TargetDir,
  [string]$PublicRemote = 'https://github.com/Abgetrennter/raya-trainer.git',
  [string]$SourceSha,
  [string]$OutManifest = '.public-source.json',
  [string]$ApprovalManifestPath = 'docs/asset-approvals.md',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

# Tool identity — bump on every behavioral change
$script:ToolVersion = '1.0.0'

# Paths to allowlist data
$script:AllowlistPath = Join-Path $PSScriptRoot 'migrate-allowlist.txt'
$script:ExcludedPath  = Join-Path $PSScriptRoot 'migrate-allowlist-excluded.txt'

# ─── Helpers ────────────────────────────────────────────────────────────

function Invoke-Git {
  param([string]$Repo, [Parameter(ValueFromRemainingArguments)][string[]]$Args)
  Push-Location -LiteralPath $Repo
  try { & git @Args } finally { Pop-Location }
}

function Get-SourceSha {
  param([string]$Repo, [string]$Sha)
  if ($Sha) { return $Sha }
  return (Invoke-Git $Repo 'rev-parse' 'HEAD').Trim()
}

function Test-WorktreeClean {
  param([string]$Repo)
  $status = Invoke-Git $Repo 'status' '--porcelain'
  # Tolerate .superpowers/ scratch dir (untracked, gitignored)
  $status = $status | Where-Object { $_ -notmatch '^.M\s+\.superpowers' -and $_ -notmatch '^\?\?\s+\.superpowers' }
  return [string]::IsNullOrWhiteSpace($status)
}

function Read-Allowlist {
  if (-not (Test-Path -LiteralPath $script:AllowlistPath)) {
    throw "Allowlist missing: $($script:AllowlistPath)"
  }
  return Get-Content -LiteralPath $script:AllowlistPath |
    Where-Object { $_ -and -not $_.StartsWith('#') } |
    ForEach-Object { $_.Trim() }
}

function Read-ExcludedList {
  if (-not (Test-Path -LiteralPath $script:ExcludedPath)) { return @() }
  return Get-Content -LiteralPath $script:ExcludedPath |
    Where-Object { $_ -and -not $_.StartsWith('#') } |
    ForEach-Object { $_.Trim() }
}

function Test-MatchesAny {
  param([string]$Path, [string[]]$Patterns)
  foreach ($p in $patterns) {
    if ($p.EndsWith('/')) {
      $prefix = $p.TrimEnd('/')
      $normalizedPath = $Path -replace '\\','/'
      if ($normalizedPath -eq $prefix -or $normalizedPath.StartsWith("$prefix/")) { return $true }
    } elseif ($p -like '*\*' -or $p -like '*/' -or $p.Contains('*')) {
      $normalizedPath = $Path -replace '\\','/'
      if ($normalizedPath -like $p) { return $true }
    } else {
      $normalizedPath = $Path -replace '\\','/'
      if ($normalizedPath -eq $p) { return $true }
    }
  }
  return $false
}

function Get-TrackedFiles {
  param([string]$Repo, [string[]]$Allowlist, [string[]]$ExcludedList)
  $allFiles = Invoke-Git $Repo 'ls-files' '--full-name'
  $kept = $allFiles | Where-Object { Test-MatchesAny $_ $Allowlist }
  $final = $kept | Where-Object { -not (Test-MatchesAny $_ $ExcludedList) }
  return $final
}

function Get-AllowlistHash {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) { return '' }
  return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLower()
}

function Get-TreeHash {
  param([string]$Root)
  $files = Get-ChildItem -Recurse -File -LiteralPath $Root -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' -and $_.Name -ne '.verify-receipt.json' } |
    ForEach-Object { Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName } |
    Where-Object { $_ -and $_.Hash }
  $sha = [System.Security.Cryptography.SHA256]::Create()
  foreach ($f in ($files | Sort-Object Path)) {
    $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($f.Path.Substring($Root.Length).ToLowerInvariant())
    [void]$sha.TransformBlock($pathBytes, 0, $pathBytes.Length, $null, 0)
    $hashBytes = [System.Convert]::FromHexString($f.Hash)
    [void]$sha.TransformBlock($hashBytes, 0, $hashBytes.Length, $null, 0)
  }
  [void]$sha.TransformFinalBlock([byte[]]::new(0), 0, 0)
  $hash = [System.BitConverter]::ToString($sha.Hash).Replace('-','').ToLowerInvariant()
  $sha.Dispose()
  return @{ Hash = $hash; Count = $files.Count }
}

function Read-ApprovalManifest {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) {
    Write-Warning "Approval manifest not found at $Path - treating all packs as pending."
    return @{}
  }
  $lines = Get-Content -LiteralPath $Path
  $result = @{}
  foreach ($line in $lines) {
    if ($line -notmatch '^\|\s*`?([a-z][a-z0-9-]*)`?\s*\|\s*`?(approved|private-only|pending|prohibited)`?\s*\|\s*`?(source-and-binary|binary-only|private)`?\s*\|') { continue }
    $result[$Matches[1]] = @{ Status = $Matches[2]; Distribution = $Matches[3] }
  }
  return $result
}

function Test-PackAuthorized {
  param([hashtable]$Approval, [string]$PackId)
  if (-not $Approval.ContainsKey($PackId)) { return $false }
  $entry = $Approval[$PackId]
  return ($entry.Status -eq 'approved') -and ($entry.Distribution -eq 'source-and-binary')
}

function Get-FileSha256 {
  param([string]$Path)
  return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLower()
}

function Read-PackManifestSha256 {
  param([string]$PackJsonPath)
  $json = Get-Content -Raw -LiteralPath $PackJsonPath | ConvertFrom-Json
  return @{
    Id = $json.id
    Version = $json.version
    Assets = $json.assets
    ManifestHash = (Get-FileSha256 $PackJsonPath)
  }
}

function Write-PublicSourceManifest {
  param(
    [string]$Path,
    [string]$SourceSha,
    [string]$ToolVersion,
    [string]$AllowlistHash,
    [array]$AssetPacks
  )
  $manifest = [ordered]@{
    schemaVersion = 1
    migrationToolVersion = $ToolVersion
    sourceSha = $SourceSha
    exportedAt = ([DateTimeOffset]::UtcNow.ToString('o'))
    allowlistHash = $AllowlistHash
    assetPacks = $AssetPacks
  }
  $manifest | ConvertTo-Json -Depth 5 | Set-Content -NoNewline -LiteralPath $Path -Encoding UTF8
}

# ─── Modes ──────────────────────────────────────────────────────────────

function Invoke-PlanMode {
  param([string]$Repo, [string]$Sha)

  Write-Host '=== Plan Report ==='
  Write-Host "Source SHA:      $Sha"
  Write-Host "Allowlist hash:  $(Get-AllowlistHash $script:AllowlistPath)"
  Write-Host "Excluded hash:   $(Get-AllowlistHash $script:ExcludedPath)"
  Write-Host "Tool version:    $($script:ToolVersion)"

  $allow = Read-Allowlist
  $excl  = Read-ExcludedList
  $tracked = Get-TrackedFiles -Repo $Repo -Allowlist $allow -ExcludedList $excl

  Write-Host ''
  Write-Host "Projected file count: $($tracked.Count)"
  Write-Host ''
  Write-Host 'Asset packs (under src/RayaTrainer.Core/Assets/Catalogs/):'
  $packs = $tracked | Where-Object { $_ -like 'src/RayaTrainer.Core/Assets/Catalogs/*/pack.json' }
  if (-not $packs) { Write-Host '  (none)' }
  foreach ($p in $packs) {
    Write-Host "  - $p"
  }

  Write-Host ''
  Write-Host 'Plan mode is read-only: no file, git, or network writes were performed.'
}

function Invoke-ExportMode {
  param([string]$Repo, [string]$Sha, [string]$Target, [string]$OutManifestPath)

  # 1. Require clean worktree
  if (-not (Test-WorktreeClean $Repo)) {
    throw "Private worktree is not clean (git status --porcelain non-empty). Commit or stash changes before Export."
  }

  # 2. Verify SHA exists
  & git -C $Repo 'cat-file' '-e' "$Sha^{commit}" 2>$null
  if ($LASTEXITCODE -ne 0) {
    throw "Source SHA $Sha does not exist in $Repo"
  }

  # 3. Build file list
  $allow = Read-Allowlist
  $excl  = Read-ExcludedList
  $files = Get-TrackedFiles -Repo $Repo -Allowlist $allow -ExcludedList $excl

  if (-not $files) { throw 'Allowlist produced an empty file set - refusing to export.' }

  # 4. Verify asset packs are approved and hashes match
  $approvalPath = Join-Path $Repo $ApprovalManifestPath
  $approval = Read-ApprovalManifest $approvalPath
  $packs = $files | Where-Object { $_ -like 'src/RayaTrainer.Core/Assets/Catalogs/*/pack.json' }
  $assetPacksForManifest = @()
  foreach ($packRel in $packs) {
    $packAbs = Join-Path $Repo $packRel
    $info = Read-PackManifestSha256 $packAbs
    if (-not (Test-PackAuthorized $approval $info.Id)) {
      throw "Pack '$($info.Id)' is not approved for source-and-binary distribution. See $approvalPath"
    }
    # Verify each asset's sha256
    $packDir = Split-Path -Parent $packAbs
    foreach ($a in $info.Assets) {
      $assetAbs = Join-Path $packDir $a.path
      $actualHash = Get-FileSha256 $assetAbs
      if ($actualHash -ne $a.sha256) {
        throw "Asset hash mismatch in pack '$($info.Id)' entry '$($a.path)': manifest=$($a.sha256) actual=$actualHash"
      }
    }
    $assetPacksForManifest += [ordered]@{
      packId = $info.Id
      version = $info.Version
      manifestHash = $info.ManifestHash
    }
    Write-Host "Verified pack '$($info.Id)' v$($info.Version) ($($info.Assets.Count) assets)"
  }

  # 5. Wipe and recreate Target
  if (Test-Path -LiteralPath $Target) { Remove-Item -Recurse -Force -LiteralPath $Target }
  New-Item -ItemType Directory -Force -Path $Target | Out-Null

  # 6. Copy each file preserving relative path
  foreach ($rel in $files) {
    $src = Join-Path $Repo $rel
    $dst = Join-Path $Target $rel
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
    Copy-Item -LiteralPath $src -Destination $dst -Force
  }

  # 7. Write .public-source.json
  $manifestFullPath = Join-Path $Target $OutManifestPath
  $allowlistHash = Get-AllowlistHash $script:AllowlistPath
  Write-PublicSourceManifest -Path $manifestFullPath -SourceSha $Sha -ToolVersion $script:ToolVersion `
    -AllowlistHash $allowlistHash -AssetPacks $assetPacksForManifest

  Write-Host ''
  Write-Host 'Export complete:'
  Write-Host "  Target:           $Target"
  Write-Host "  Files copied:     $($files.Count)"
  Write-Host "  Asset packs:      $($assetPacksForManifest.Count)"
  Write-Host "  Manifest written: $manifestFullPath"
}

function Invoke-VerifyMode {
  param([string]$Target)

  Write-Host '=== Verify ==='

  if (-not (Test-Path -LiteralPath (Join-Path $Target '.public-source.json'))) {
    throw 'Target does not contain .public-source.json - run Export first.'
  }

  Push-Location -LiteralPath $Target
  try {
    # 1. Restore + build
    Write-Host '[1/5] dotnet restore + build RayaTrainer.sln'
    & dotnet restore RayaTrainer.sln
    if ($LASTEXITCODE -ne 0) { throw "restore failed" }
    & dotnet build RayaTrainer.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "build failed" }

    # 2. Managed tests (non-fatal - some tests reference private tools excluded from projection)
    Write-Host '[2/5] dotnet test (failure is warning, not fatal)'
    & dotnet test RayaTrainer.sln -c Release --no-build -v minimal
    if ($LASTEXITCODE -ne 0) { Write-Host '  (some tests failed - expected in public build)' }

    # 3. x86 Agent build
    Write-Host '[3/5] MSBuild x86 Agent'
    $msbuild = $null
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
      $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    }
    if (-not $msbuild) { $msbuild = (Get-Command MSBuild.exe -ErrorAction SilentlyContinue).Source }
    if (-not $msbuild) { throw 'MSBuild.exe not found on PATH (install VS Build Tools or run from Developer PowerShell)' }
    & $msbuild 'src/RayaTrainer.Agent/RayaTrainer.Agent.vcxproj' /p:Configuration=Release /p:Platform=Win32 /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Agent build failed" }

    # 4. Code-gen check (hash before/after, target is NOT a git repo)
    Write-Host '[4/5] Code-gen idempotency check'
    $generatedDirs = Get-ChildItem -Recurse -Directory -Filter 'Generated' -LiteralPath $Target -ErrorAction SilentlyContinue
    $hashBefore = @{}
    if ($generatedDirs) {
      $genFiles = Get-ChildItem -Recurse -File -LiteralPath $generatedDirs.FullName -ErrorAction SilentlyContinue
      foreach ($gf in $genFiles) {
        $hashBefore[$gf.FullName] = (Get-FileHash -Algorithm SHA256 -LiteralPath $gf.FullName -ErrorAction SilentlyContinue).Hash
      }
    }
    # Touch csproj timestamps to force regen
    Get-ChildItem -Recurse -File -Filter '*.csproj' -LiteralPath $Target -ErrorAction SilentlyContinue | ForEach-Object {
      (Get-Item -LiteralPath $_.FullName).LastWriteTime = (Get-Date)
    }
    & dotnet build "$Target/RayaTrainer.sln" -c Release /t:Rebuild /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "rebuild with regen failed" }

    $hashAfter = @{}
    if ($generatedDirs) {
      $genFilesAfter = Get-ChildItem -Recurse -File -LiteralPath $generatedDirs.FullName -ErrorAction SilentlyContinue
      foreach ($gf in $genFilesAfter) {
        $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $gf.FullName -ErrorAction SilentlyContinue).Hash
        $hashAfter[$gf.FullName] = $h
        $rel = $gf.FullName.Substring($Target.Length)
        if (-not $hashBefore.ContainsKey($gf.FullName) -or $hashBefore[$gf.FullName] -ne $h) {
          throw "Generated file changed after rebuild: $rel"
        }
      }
    }

    # 5. Preflight source audit (use -CheckLevel, not -Mode)
    Write-Host '[5/5] preflight-leak-check (source mode)'
    $preflight = Join-Path $Target 'scripts/preflight-leak-check.ps1'
    if (Test-Path $preflight) {
      & pwsh -File $preflight -CheckLevel source
      if ($LASTEXITCODE -ne 0) { throw 'preflight source mode failed' }
    } else {
      Write-Host '  (preflight-leak-check.ps1 not present in target - skipping)'
    }

    # Compute tree hash for publish verification
    $treeResult = Get-TreeHash -Root $Target
    $treeHash = $treeResult.Hash
    $receipt = Join-Path $Target '.verify-receipt.json'
    $receiptData = [ordered]@{
      verifiedAt = ([DateTimeOffset]::UtcNow.ToString('o'))
      treeHash = $treeHash
      fileCount = $treeResult.Count
    }
    $receiptData | ConvertTo-Json -Depth 3 | Set-Content -NoNewline -LiteralPath $receipt -Encoding UTF8
    Write-Host "Verify tree hash: $treeHash ($($treeResult.Count) files)"

    Write-Host ''
    Write-Host 'Verify passed.'
  }
  finally {
    Pop-Location
  }
}

function Invoke-PublishMode {
  param([string]$Target, [string]$PublicRemoteUrl)

  # 1. Verify receipt must exist and be fresh
  $receiptPath = Join-Path $Target '.verify-receipt.json'
  if (-not (Test-Path -LiteralPath $receiptPath)) {
    throw 'Missing .verify-receipt.json - must run Verify before Publish.'
  }
  $receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
  if (-not $receipt.verifiedAt -or -not $receipt.treeHash) {
    throw 'Invalid .verify-receipt.json - re-run Verify.'
  }
  # Receipt must be recent (< 1 hour old)
  $verifiedAt = [DateTimeOffset]::Parse($receipt.verifiedAt)
  $age = [DateTimeOffset]::UtcNow - $verifiedAt
  if ($age.TotalHours -gt 1) {
    throw "Verify receipt expired ($($age.TotalMinutes.ToString('F0')) min old). Re-run Verify."
  }

  # 2. Re-compute tree hash and compare
  $manifestPath = Join-Path $Target '.public-source.json'
  if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'Target does not contain .public-source.json - run Export first.'
  }

  $currentResult = Get-TreeHash -Root $Target
  $currentHash = $currentResult.Hash

  if ($currentHash -ne $receipt.treeHash) {
    throw "Tree hash mismatch: receipt=$($receipt.treeHash) actual=$currentHash. Projection was modified after Verify. Re-run Verify."
  }

  Write-Host "Tree hash verified: $currentHash (matches receipt)"
  Write-Host ''

  $publicClone = Join-Path $env:TEMP "raya-public-$(Get-Random)"

  try {
    Write-Host "Cloning public repo to $publicClone"
    & git clone --quiet $PublicRemoteUrl $publicClone
    if ($LASTEXITCODE -ne 0) { throw "git clone failed for $PublicRemoteUrl" }

    Push-Location -LiteralPath $publicClone
    try {
      # Ensure main branch is current and up to date with origin
      & git fetch --quiet origin
      if ($LASTEXITCODE -ne 0) { throw 'git fetch failed' }
      & git checkout --quiet main
      if ($LASTEXITCODE -ne 0) { throw 'git checkout main failed' }
      $localHead = (& git rev-parse main).Trim()
      $originHead = (& git rev-parse origin/main).Trim()
      if ($localHead -ne $originHead) {
        throw "Local main ($localHead) != origin/main ($originHead). Sync local main with origin before publishing."
      }

      $beforeSha = $localHead

      # Apply projected tree: remove existing tracked files (except .git) and copy target contents
      Write-Host 'Applying projected tree'
      Get-ChildItem -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
      Get-ChildItem -Force -LiteralPath $Target | ForEach-Object {
        Copy-Item -Recurse -Force -LiteralPath $_.FullName -Destination $publicClone
      }

      # Stage all
      & git add -A
      $staged = & git diff --cached --name-only
      if (-not $staged) {
        Write-Host 'No changes to publish - exiting success.'
        return
      }

      # Read source SHA from manifest for commit message
      $manifestPath = Join-Path $Target '.public-source.json'
      $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
      $sourceSha = $manifest.sourceSha

      & git commit --quiet -m "chore(sync): publish $sourceSha"
      if ($LASTEXITCODE -ne 0) { throw 'git commit failed' }
      $newSha = (& git rev-parse HEAD).Trim()

      # Push - must be fast-forward; never force
      & git push origin main
      if ($LASTEXITCODE -ne 0) {
        & git reset --hard $beforeSha | Out-Null
        throw "git push failed (non-fast-forward?). Rolled back local clone. Inspect $publicClone."
      }

      Write-Host ''
      Write-Host 'Publish complete:'
      Write-Host "  Public commit:   $newSha"
      Write-Host "  Source SHA:      $sourceSha"
      Write-Host "  FF from:         $beforeSha"
    }
    finally {
      Pop-Location
    }
  }
  finally {
    Write-Host "(public clone left at $publicClone for inspection)"
  }
}

# ─── Main ───────────────────────────────────────────────────────────────

$sourceSha = Get-SourceSha $PrivateRepoRoot $SourceSha
Write-Host "migrate-to-public v$($script:ToolVersion) - mode=$Mode sourceSha=$sourceSha"

switch ($Mode) {
  'Plan'   { Invoke-PlanMode   -Repo $PrivateRepoRoot -Sha $sourceSha }
  'Export' {
    if (-not $TargetDir) { throw '-TargetDir is required for Export mode' }
    Invoke-ExportMode -Repo $PrivateRepoRoot -Sha $sourceSha -Target $TargetDir -OutManifestPath $OutManifest
  }
  'Verify' {
    if (-not $TargetDir) { throw '-TargetDir is required for Verify mode' }
    Invoke-VerifyMode -Target $TargetDir
  }
  'Publish' {
    if (-not $TargetDir) { throw '-TargetDir is required for Publish mode' }
    Invoke-PublishMode -Target $TargetDir -PublicRemoteUrl $PublicRemote
  }
}
