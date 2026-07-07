param(
    [Parameter(Mandatory = $true)]
    [string[]]$ZipPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

$textExtensions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@(
    ".json",
    ".config",
    ".txt",
    ".xml",
    ".ps1",
    ".cmd",
    ".bat",
    ".yml",
    ".yaml",
    ".md",
    ".ini",
    ".log",
    ".csv"
) | ForEach-Object { [void]$textExtensions.Add($_) }

function Normalize-ZipEntryName {
    param([string]$Name)
    return $Name.Replace("\", "/").TrimStart("/")
}

function Get-ForbiddenEntryReason {
    param([string]$EntryName)

    $normalized = Normalize-ZipEntryName $EntryName
    $leafName = Split-Path -Leaf $normalized

    if ($leafName.Equals("RayaTrainer.settings.json", [StringComparison]::OrdinalIgnoreCase)) {
        return "user settings file RayaTrainer.settings.json must not be distributed"
    }

    if ($normalized.Equals("analysis", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("analysis/", [StringComparison]::OrdinalIgnoreCase)) {
        return "analysis/ must not be distributed"
    }

    if ($leafName.Equals("Trainer.exe", [StringComparison]::OrdinalIgnoreCase) -or
        $leafName.Equals("RedAlert3_Uprising_Trainer_FINAL.exe", [StringComparison]::OrdinalIgnoreCase)) {
        return "original trainer executable must not be distributed"
    }

    if ($normalized.Equals(".codex", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith(".codex/", [StringComparison]::OrdinalIgnoreCase)) {
        return ".codex/ must not be distributed"
    }

    if ($normalized.Equals("tools", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("tools/", [StringComparison]::OrdinalIgnoreCase)) {
        return "tools/ must not be distributed"
    }

    return $null
}

function Test-TextEntry {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)

    $extension = [System.IO.Path]::GetExtension($Entry.FullName)
    return $textExtensions.Contains($extension)
}

function Read-ZipTextEntry {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)

    $stream = $Entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

$violations = [System.Collections.Generic.List[string]]::new()
$blockedAgentDllDependencies = @(
    "VCRUNTIME140.dll",
    "VCRUNTIME",
    "MSVCP140.dll",
    "MSVCP",
    "ucrtbase.dll",
    "kubazip.dll"
)

function Read-ZipBinaryEntry {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)

    $stream = $Entry.Open()
    try {
        $memory = [System.IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            return $memory.ToArray()
        }
        finally {
            $memory.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-AsciiBytesContain {
    param(
        [byte[]]$Bytes,
        [string]$Text
    )

    $needle = [System.Text.Encoding]::ASCII.GetBytes($Text)
    if ($needle.Length -eq 0 -or $Bytes.Length -lt $needle.Length) {
        return $false
    }

    for ($offset = 0; $offset -le $Bytes.Length - $needle.Length; $offset++) {
        $matched = $true
        for ($index = 0; $index -lt $needle.Length; $index++) {
            if ($Bytes[$offset + $index] -ne $needle[$index]) {
                $matched = $false
                break
            }
        }

        if ($matched) {
            return $true
        }
    }

    return $false
}

function Get-AgentDllValidationReasons {
    param([byte[]]$Bytes)

    $reasons = [System.Collections.Generic.List[string]]::new()
    if ($Bytes.Length -lt 64 -or $Bytes[0] -ne 0x4D -or $Bytes[1] -ne 0x5A) {
        $reasons.Add("RayaTrainer.Agent.dll must be a PE file")
        return $reasons
    }

    $peOffset = [BitConverter]::ToUInt32($Bytes, 0x3C)
    if ($peOffset -gt [int]::MaxValue -or $Bytes.Length -lt ([int]$peOffset + 24)) {
        $reasons.Add("RayaTrainer.Agent.dll has an invalid PE header")
        return $reasons
    }

    $peOffset = [int]$peOffset
    $signature = [BitConverter]::ToUInt32($Bytes, $peOffset)
    if ($signature -ne 0x00004550) {
        $reasons.Add("RayaTrainer.Agent.dll has an invalid PE signature")
        return $reasons
    }

    $machine = [BitConverter]::ToUInt16($Bytes, $peOffset + 4)
    if ($machine -ne 0x014C) {
        $reasons.Add(("RayaTrainer.Agent.dll must be x86; PE machine is 0x{0:X4}" -f $machine))
    }

    $characteristics = [BitConverter]::ToUInt16($Bytes, $peOffset + 22)
    if (($characteristics -band 0x2000) -eq 0) {
        $reasons.Add("RayaTrainer.Agent.dll must have the PE DLL characteristic")
    }

    foreach ($dependency in $blockedAgentDllDependencies) {
        if (Test-AsciiBytesContain -Bytes $Bytes -Text $dependency) {
            $reasons.Add("RayaTrainer.Agent.dll contains blocked native dependency marker: $dependency")
        }
    }

    return $reasons
}

foreach ($path in $ZipPath) {
    $resolvedPath = Resolve-Path -LiteralPath $path -ErrorAction Stop
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath.Path)
    $hasAgentDll = $false
    try {
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.FullName)) {
                continue
            }

            $normalized = Normalize-ZipEntryName $entry.FullName
            $leafName = Split-Path -Leaf $normalized
            if ($leafName.Equals("RayaTrainer.Agent.dll", [StringComparison]::OrdinalIgnoreCase)) {
                $hasAgentDll = $true
                $agentDllBytes = Read-ZipBinaryEntry $entry
                foreach ($agentReason in (Get-AgentDllValidationReasons $agentDllBytes)) {
                    $violations.Add("$($resolvedPath.Path): $normalized - $agentReason")
                }
            }

            $reason = Get-ForbiddenEntryReason $normalized
            if ($null -ne $reason) {
                $violations.Add("$($resolvedPath.Path): $normalized - $reason")
            }

            if (-not [string]::IsNullOrEmpty($entry.Name) -and (Test-TextEntry $entry)) {
                $content = Read-ZipTextEntry $entry
                if ($content -match "[A-Za-z]:\\[^\r\n`"]+") {
                    $violations.Add("$($resolvedPath.Path): $normalized - contains a local Windows path")
                }
            }
        }

        if (-not $hasAgentDll) {
            $violations.Add("$($resolvedPath.Path): missing RayaTrainer.Agent.dll")
        }
    }
    finally {
        $archive.Dispose()
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Release package validation failed:"
    foreach ($violation in $violations) {
        Write-Host " - $violation"
    }
    exit 1
}

foreach ($path in $ZipPath) {
    Write-Host "Validated release package: $path"
}
