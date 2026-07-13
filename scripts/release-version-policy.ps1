[CmdletBinding()]
param(
  [string]$CandidateTag,
  [AllowEmptyString()] [string]$PreviousTag
)

$ErrorActionPreference = 'Stop'

function ConvertTo-ReleaseVersion {
  param([Parameter(Mandatory)] [string]$Tag)

  if ($Tag -notmatch '^v(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)$') {
    throw "Release tag must use strict vMAJOR.MINOR.PATCH format: $Tag"
  }

  $major = [int]$Matches.major
  $minor = [int]$Matches.minor
  $patch = [int]$Matches.patch
  return [pscustomobject]@{
    Tag = $Tag
    Major = $major
    Minor = $minor
    Patch = $patch
    Version = [Version]::new($major, $minor, $patch)
  }
}

function Get-LatestReleaseTag {
  param(
    [AllowEmptyCollection()] [string[]]$Tags,
    [string]$ExcludeTag
  )

  $versions = @(
    foreach ($tag in $Tags) {
      if ([string]::IsNullOrWhiteSpace($tag) -or $tag -eq $ExcludeTag) { continue }
      ConvertTo-ReleaseVersion -Tag $tag.Trim()
    }
  )
  if ($versions.Count -eq 0) { return $null }
  return ($versions | Sort-Object Version -Descending | Select-Object -First 1).Tag
}

function Assert-ReleaseVersionStep {
  param(
    [Parameter(Mandatory)] [string]$CandidateTag,
    [AllowEmptyString()] [string]$PreviousTag
  )

  $candidate = ConvertTo-ReleaseVersion -Tag $CandidateTag
  if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
    if ($candidate.Tag -ne 'v0.1.0') {
      throw "The first public release must be v0.1.0, not $CandidateTag."
    }
    return
  }

  $previous = ConvertTo-ReleaseVersion -Tag $PreviousTag
  $isNextPatch = $candidate.Major -eq $previous.Major `
    -and $candidate.Minor -eq $previous.Minor `
    -and $candidate.Patch -eq ($previous.Patch + 1)
  $isNextMinor = $candidate.Major -eq $previous.Major `
    -and $candidate.Minor -eq ($previous.Minor + 1) `
    -and $candidate.Patch -eq 0

  if (-not ($isNextPatch -or $isNextMinor)) {
    throw "Release version must be the next patch or next minor after $PreviousTag; major jumps and skipped versions are forbidden: $CandidateTag"
  }
}

if (-not [string]::IsNullOrWhiteSpace($CandidateTag)) {
  Assert-ReleaseVersionStep -CandidateTag $CandidateTag -PreviousTag $PreviousTag
  Write-Host "Release version policy passed: $PreviousTag -> $CandidateTag"
}
