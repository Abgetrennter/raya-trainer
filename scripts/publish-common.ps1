function Get-BuildVersionMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [string]$BuildTag
    )

    $informationalVersion = Resolve-BuildInformationalVersion -RepoRoot $RepoRoot -BuildTag $BuildTag
    $packageVersion = ConvertTo-PackageVersion -InformationalVersion $informationalVersion
    $numericVersion = ConvertTo-NumericAssemblyVersion -PackageVersion $packageVersion

    [pscustomobject]@{
        PackageVersion = $packageVersion
        InformationalVersion = $informationalVersion
        NumericVersion = $numericVersion
    }
}

function Resolve-BuildInformationalVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [string]$BuildTag
    )

    if (-not [string]::IsNullOrWhiteSpace($BuildTag)) {
        return $BuildTag.Trim()
    }

    Push-Location $RepoRoot
    try {
        try {
            $tag = git describe --tags --exact-match 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($tag)) {
                return $tag.Trim()
            }
        } catch {}

        try {
            $hash = git rev-parse --short HEAD 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($hash)) {
                return "0.0.0-dev+$($hash.Trim())"
            }
        } catch {}

        return "0.0.0-local"
    }
    finally {
        Pop-Location
    }
}

function ConvertTo-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InformationalVersion
    )

    $version = $InformationalVersion.Trim()
    if ($version.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $version = $version.Substring(1)
    }

    return $version
}

function ConvertTo-NumericAssemblyVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageVersion
    )

    $version = $PackageVersion.Trim()
    $suffixIndex = $version.IndexOfAny([char[]]@("-", "+"))
    if ($suffixIndex -ge 0) {
        $version = $version.Substring(0, $suffixIndex)
    }

    $parts = @($version.Split("."))
    if ($parts.Count -lt 3) {
        throw "Build version must include major, minor and patch parts: $PackageVersion"
    }

    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    return ($parts[0..3] -join ".")
}

function Clear-PublishOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$PublishRoot,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    $publishRoot = [System.IO.Path]::GetFullPath($PublishRoot).TrimEnd([char[]]@("\", "/"))
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath).TrimEnd([char[]]@("\", "/"))
    $requiredPrefix = "$publishRoot$([System.IO.Path]::DirectorySeparatorChar)"
    if (-not $resolvedOutput.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear publish output outside publish root: $resolvedOutput"
    }

    if (Test-Path -LiteralPath $resolvedOutput) {
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
}

function Resolve-MSBuildPath {
    $knownPaths = @(
        "${env:ProgramFiles}/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe",
        "${env:ProgramFiles}/Microsoft Visual Studio/2022/Professional/MSBuild/Current/Bin/MSBuild.exe",
        "${env:ProgramFiles}/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe",
        "${env:ProgramFiles(x86)}/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/MSBuild.exe"
    )

    foreach ($path in $knownPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
            return $path
        }
    }

    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $resolved = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild/**/Bin/MSBuild.exe" | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    return "MSBuild.exe"
}

function Build-AgentDll {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $project = Join-Path $RepoRoot "src/RayaTrainer.Agent/RayaTrainer.Agent.vcxproj"
    $msbuild = Resolve-MSBuildPath
    & $msbuild $project /p:Configuration=Release /p:Platform=Win32 /m
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build RayaTrainer.Agent.dll."
    }
}

function Copy-AgentDll {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    $agentDll = Join-Path $RepoRoot "artifacts/native/Release/Win32/RayaTrainer.Agent.dll"
    if (-not (Test-Path -LiteralPath $agentDll)) {
        throw "Missing native Agent DLL: $agentDll"
    }

    Copy-Item -LiteralPath $agentDll -Destination (Join-Path $OutputPath "RayaTrainer.Agent.dll") -Force
}

function Copy-RuntimeCheckScripts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    $scriptsDir = Join-Path $RepoRoot "scripts"
    $checkScript = Join-Path $scriptsDir "check-dotnet-runtime.ps1"
    $launcherScript = Join-Path $scriptsDir "启动修改器.bat"

    if (Test-Path -LiteralPath $checkScript) {
        Copy-Item -LiteralPath $checkScript -Destination (Join-Path $OutputPath "check-dotnet-runtime.ps1") -Force
    }

    if (Test-Path -LiteralPath $launcherScript) {
        Copy-Item -LiteralPath $launcherScript -Destination (Join-Path $OutputPath "启动修改器.bat") -Force
    }
}
