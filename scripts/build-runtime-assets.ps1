<#
.SYNOPSIS
    Builds the runtime asset bundle streams (Game 6 + Game 7) from source.

.DESCRIPTION
    Single source of truth for regenerating the RayaTrainer runtime asset
    bundle. Git carries only text truth (Mod.xml.source, asset-manifest.json,
    schema); the eight binary streams are local build products:

      Game 6 (RA3 1.12):  stage -> SDK-X BinaryAssetBuilder -> provenance
                          normalization -> HashFix -> ModAssetResolver art
                          merge -> AssetsRoot\mod.*
      Game 7 (Uprising):  strip GameObjects/art includes from Mod.xml.source
                          -> same BAB build -> normalize -> HashFix ->
                          RA3-Uprising-Converter (v7 manifest convert +
                          bin/imp/relo header patch) -> AssetsRoot\uprising\mod.*

    Provenance normalization: BAB embeds the build machine's rooted staging
    path in the v7 manifest tail (sources region; field@44 = region size,
    type-table entry @28 = string offset relative to the region start). The
    path prefix is rewritten to the canonical 'Mod.xml' and every non-zero
    @28 is shifted by the same delta, making all outputs byte-identical on
    every machine and path (verified 2026-08-17, plan Phase 0).

    After building, asset-manifest.json stream hashes are recomputed in place.
    Template (name) changes between Mod.xml.source and the manifest table are
    refused with a diff - add/remove rows manually (with metadata) and re-run.

.PARAMETER AssetsRoot
    Directory holding Mod.xml.source, asset-manifest.json and the stream
    outputs. Defaults to the Core RuntimeAssets location.

.PARAMETER SdkRoot
    RA3 MOD SDK-X root (BinaryAssetBuilder/HashFix/ModAssetResolver +
    builtmods\sagexml baselines). Defaults to the workspace references
    mirror; pass explicitly outside the workspace.

.PARAMETER ConverterRoot
    RA3-Uprising-Converter project root (exe at
    src\Converter\bin\Release\net9.0\uprising-converter.exe, truth tables at
    data\truth). Defaults to the workspace copy; required for the Game 7
    variant.

.PARAMETER WorkRoot
    Scratch directory for staging/raw/resolver state. Defaults to
    artifacts\runtime-assets-build under the repository (gitignored).

.PARAMETER VerifyOnly
    Skip the build; compare on-disk stream hashes against asset-manifest.json.
    Exit codes: 0 = match, 1 = hash mismatch, 2 = streams missing.

.EXAMPLE
    pwsh -File scripts/build-runtime-assets.ps1
    pwsh -File scripts/build-runtime-assets.ps1 -VerifyOnly
#>
[CmdletBinding()]
param(
    [string]$AssetsRoot,
    [string]$SdkRoot,
    [string]$ConverterRoot,
    [string]$WorkRoot,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $AssetsRoot) {
    $AssetsRoot = Join-Path $repoRoot 'src\RayaTrainer.Core\RuntimeAssets\AttributeModifiers'
}
if (-not $WorkRoot) {
    $WorkRoot = Join-Path $repoRoot 'artifacts\runtime-assets-build'
}

# Workspace resolution: walk up from the repo to ra3code.config.json. Explicit
# parameters always win; config values beat defaults so one workspace serves
# all machines without hardcoded absolute paths in the repo.
$workspaceRoot = $null
$cursor = $repoRoot
while ($cursor) {
    if (Test-Path -LiteralPath (Join-Path $cursor 'ra3code.config.json')) { $workspaceRoot = $cursor; break }
    $cursor = Split-Path $cursor -Parent
}
$workspaceConfig = $null
if ($workspaceRoot) {
    $workspaceConfig = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'ra3code.config.json') | ConvertFrom-Json
}
if (-not $SdkRoot) {
    $references = if ($env:RA3CODE_REFERENCES) { $env:RA3CODE_REFERENCES }
    elseif ($workspaceConfig) { Join-Path $workspaceRoot $workspaceConfig.projects.references.path }
    $SdkRoot = if ($references) { Join-Path $references 'RA3-MODSDK-X' } else { 'E:\Code\Ra3Code\community\references\RA3-MODSDK-X' }
}
if (-not $ConverterRoot) {
    $ConverterRoot = if ($workspaceRoot) { Join-Path $workspaceRoot 'personal\RA3-Uprising-Converter' } else { 'E:\Code\Ra3Code\personal\RA3-Uprising-Converter' }
}

$builder = Join-Path $SdkRoot 'tools\BinaryAssetBuilder.exe'
$hashFix = Join-Path $SdkRoot 'tools\HashFix.exe'
$resolver = Join-Path $SdkRoot 'tools\ModAssetResolver.exe'
$converter = Join-Path $ConverterRoot 'src\Converter\bin\Release\net9.0\uprising-converter.exe'
$truthDir = Join-Path $ConverterRoot 'data\truth'
$sdkBaseline = Join-Path $SdkRoot 'builtmods\sagexml'
$sourceXml = Join-Path $AssetsRoot 'Mod.xml.source'
$manifestPath = Join-Path $AssetsRoot 'asset-manifest.json'

$streamFiles = @('mod.manifest', 'mod.bin', 'mod.relo', 'mod.imp')
$game7ExpectedCount = 485

# The arsenal objects are build-time clones of SDK templates. Renaming the source
# declaration before BAB compiles it gives every product-facing object a Raya-owned
# InstanceID without registering an override for the stock template.
$arsenalCloneSpecs = @(
    [pscustomobject]@{ Source = 'Allied\Units\AlliedMCV.xml'; SourceId = 'AlliedMCV'; Id = 'Raya_ArsenalMCV'; Role = 'mcv' },
    [pscustomobject]@{ Source = 'Allied\Structures\AlliedConstructionYard.xml'; SourceId = 'AlliedConstructionYard'; Id = 'Raya_ArsenalConstructionYard'; Role = 'yard' },
    [pscustomobject]@{ Source = 'Allied\Structures\AlliedWarFactory.xml'; SourceId = 'AlliedWarFactory'; Id = 'Raya_ArsenalWarFactory'; Role = 'factory' },
    [pscustomobject]@{ Source = 'Neutral\Structures\VeterancyTechStructure.xml'; SourceId = 'VeterancyTechStructure'; Id = 'Raya_ArsenalVeterancyCenter'; Role = 'tech' },
    [pscustomobject]@{ Source = 'Neutral\Structures\ObservationPostTechStructure.xml'; SourceId = 'ObservationPostTechStructure'; Id = 'Raya_ArsenalObservationPost'; Role = 'tech' },
    [pscustomobject]@{ Source = 'Neutral\Structures\HospitalTechStructure.xml'; SourceId = 'HospitalTechStructure'; Id = 'Raya_ArsenalHospital'; Role = 'tech' },
    [pscustomobject]@{ Source = 'Neutral\Structures\GarageTechStructure.xml'; SourceId = 'GarageTechStructure'; Id = 'Raya_ArsenalGarage'; Role = 'tech' },
    [pscustomobject]@{ Source = 'Neutral\Structures\DefensiveStructureTechStructure.xml'; SourceId = 'DefensiveStructureTechStructure'; Id = 'Raya_ArsenalDefensiveStructure'; Role = 'tech' },
    [pscustomobject]@{ Source = 'Neutral\Structures\AirportTechStructure.xml'; SourceId = 'AirportTechStructure'; Id = 'Raya_ArsenalAirport'; Role = 'tech' },
    [pscustomobject]@{ Source = 'Neutral\Structures\OilDerrick.xml'; SourceId = 'OilDerrick'; Id = 'Raya_ArsenalOilDerrick'; Role = 'tech' },
    [pscustomobject]@{ Source = 'Civilian\Brighton_Beach_BB\Buildings\BB_EuropeCoastalGun.xml'; SourceId = 'BB_EuropeCoastalGun'; Id = 'Raya_ArsenalEuropeCoastalGun'; Role = 'defense' },
    [pscustomobject]@{ Source = 'Civilian\Kremlin_KR\Buildings\KR_ArtilleryDome.xml'; SourceId = 'KR_ArtilleryDome'; Id = 'Raya_ArsenalArtilleryDome'; Role = 'defense' },
    [pscustomobject]@{ Source = 'Civilian\Floating_Island_FI\Buildings\FI_FloatingFortressMainGun.xml'; SourceId = 'FI_FloatingFortressMainGun'; Id = 'Raya_ArsenalJapanTriCannon'; Role = 'defense' },
    [pscustomobject]@{ Source = 'Civilian\Cape_Cod_CC\Buildings\CapeCod_House01.xml'; SourceId = 'CapeCod_House01'; Id = 'Raya_ArsenalCapeCodHouse'; Role = 'civilian' })

$expectedGameObjectKeys = @(
    '942FFF2D:2FA0F78E', # Raya_VeterancyAcademy
    '942FFF2D:4CF69F07', # Raya_VehicleGarage
    '942FFF2D:20293E66', # Raya_MechaKing
    '942FFF2D:D997A48C', # Raya_VisionRevealer
    '942FFF2D:04B1994D', # Raya_ArsenalMCV
    '942FFF2D:AD6ED136', # Raya_ArsenalConstructionYard
    '942FFF2D:4A235D81', # Raya_ArsenalWarFactory
    '942FFF2D:7F23FE56', # Raya_EmperorMecha
    '942FFF2D:A001F534', # Raya_Emperor
    '942FFF2D:4C0714C5', # Raya_ExplodingTengu
    '942FFF2D:EACC5F9C', # Raya_SpecialMirageTank
    '942FFF2D:7F8357F2', # Raya_PresidentialLimo
    '942FFF2D:10E4B347', # Raya_ArsenalVeterancyCenter
    '942FFF2D:7BF8CAB6', # Raya_ArsenalObservationPost
    '942FFF2D:09520923', # Raya_ArsenalHospital
    '942FFF2D:83E61B7D', # Raya_ArsenalGarage
    '942FFF2D:808CB24A', # Raya_ArsenalDefensiveStructure
    '942FFF2D:3AB7456E', # Raya_ArsenalAirport
    '942FFF2D:089DCBCB', # Raya_ArsenalOilDerrick
    '942FFF2D:44ECB74E', # Raya_ArsenalEuropeCoastalGun
    '942FFF2D:5BE5DADE', # Raya_ArsenalArtilleryDome
    '942FFF2D:55D17E6D', # Raya_ArsenalJapanTriCannon
    '942FFF2D:B1C6A450') # Raya_ArsenalCapeCodHouse

$officialDenyList = @(
    '942FFF2D:FE293C2D', '942FFF2D:3B1CD6DC', '942FFF2D:0FDB5FC4', '942FFF2D:0F71F447',
    'E86E4D61:D13CBAE8', 'E86E4D61:96AE85DB', '942FFF2D:450BD734',
    '942FFF2D:28DA574E', '942FFF2D:61A093F6', '942FFF2D:8209C058',
    '942FFF2D:CC023E30', '942FFF2D:59DBBBBA', '942FFF2D:7AE6911F',
    '942FFF2D:DD23DF49', '942FFF2D:6E3AD64E',
    '942FFF2D:AE378830', '942FFF2D:7E129BB4', '942FFF2D:A53FE0B2',
    '942FFF2D:E9C38722', '942FFF2D:A3E28BBE')

function Get-StreamSha256Upper([string]$path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToUpperInvariant()
}

function Get-FileFingerprint([string]$path) {
    '{0}  {1}' -f (Get-StreamSha256Upper $path), (Split-Path $path -Leaf)
}

function Get-ManifestAssetKeys([byte[]]$bytes) {
    $count = [BitConverter]::ToUInt32($bytes, 12)
    $keys = @(for ($i = 0; $i -lt $count; $i++) {
        $o = 48 + $i * 48
        '{0:X8}:{1:X8}' -f [BitConverter]::ToUInt32($bytes, $o), [BitConverter]::ToUInt32($bytes, $o + 4)
    })
    return @{ Count = $count; Keys = $keys }
}

function Assert-AssetGates {
    param([byte[]]$Bytes, [string[]]$ExpectedKeys, [string]$Label)

    $info = Get-ManifestAssetKeys $Bytes
    foreach ($expected in $ExpectedKeys) {
        if ($info.Keys -notcontains $expected) {
            throw "[$Label] Expected asset missing from manifest: $expected"
        }
    }
    foreach ($official in $officialDenyList) {
        if ($info.Keys -contains $official) {
            throw "[$Label] Official asset leaked into runtime package: $official"
        }
    }
    return $info.Count
}

function Copy-BaselineStreams {
    param([string]$OutputRoot)
    foreach ($entry in @(
        @('Static.manifest', 'static.manifest'),
        @('Global.manifest', 'global.manifest'),
        @('Audio.manifest', 'audio.manifest'))) {
        $baselineSource = Join-Path $sdkBaseline $entry[0]
        if (-not (Test-Path -LiteralPath $baselineSource -PathType Leaf)) {
            throw "SDK baseline manifest missing: $baselineSource"
        }
        Copy-Item -LiteralPath $baselineSource -Destination (Join-Path $OutputRoot $entry[1]) -Force
    }
}

function Write-FilteredWorldbuilderManifest {
    param([string]$OutputRoot)

    function Get-ManifestKeys([string]$path) {
        $b = [IO.File]::ReadAllBytes($path)
        $count = [BitConverter]::ToUInt32($b, 0x0C)
        $set = New-Object 'System.Collections.Generic.HashSet[uint64]'
        for ($i = 0; $i -lt $count; $i++) {
            $o = 48 + $i * 48
            [void]$set.Add(([uint64][BitConverter]::ToUInt32($b, $o) -shl 32) -bor [BitConverter]::ToUInt32($b, $o + 4))
        }
        return $set
    }

    # The official three-stream baselines resolve the reference includes; WorldBuilder art
    # collides with them on duplicate keys, so only art-only, non-duplicate entries survive.
    $seenKeys = New-Object 'System.Collections.Generic.HashSet[uint64]'
    foreach ($entry in @('Static.manifest', 'Global.manifest', 'Audio.manifest')) {
        foreach ($key in Get-ManifestKeys (Join-Path $sdkBaseline $entry)) { [void]$seenKeys.Add($key) }
    }

    $artTypes = New-Object 'System.Collections.Generic.HashSet[uint64]'
    foreach ($hex in @('F0F08712', 'C2B1A262', '61D7EA40', '21E727DA', '2448AE30', 'E3181C04')) {
        [void]$artTypes.Add([uint64][Convert]::ToUInt64($hex, 16))
    }

    $worldbuilderSource = Join-Path $sdkBaseline 'Worldbuilder.manifest'
    if (-not (Test-Path -LiteralPath $worldbuilderSource -PathType Leaf)) {
        throw "SDK baseline manifest missing: $worldbuilderSource"
    }
    $wbBytes = [IO.File]::ReadAllBytes($worldbuilderSource)
    $wbCount = [BitConverter]::ToUInt32($wbBytes, 0x0C)
    $keep = New-Object System.Collections.Generic.List[int]
    for ($i = 0; $i -lt $wbCount; $i++) {
        $o = 48 + $i * 48
        $t = [BitConverter]::ToUInt32($wbBytes, $o)
        $ii = [BitConverter]::ToUInt32($wbBytes, $o + 4)
        $key = ([uint64]$t -shl 32) -bor $ii
        if ($artTypes.Contains([uint64]$t) -and -not $seenKeys.Contains($key)) { $keep.Add($i) }
    }

    $wbHeader = [byte[]]::new(48)
    [Array]::Copy($wbBytes, 0, $wbHeader, 0, 48)
    [BitConverter]::GetBytes([uint32]$keep.Count).CopyTo($wbHeader, 0x0C)
    $wbOut = New-Object System.IO.MemoryStream
    $wbOut.Write($wbHeader, 0, 48)
    foreach ($idx in $keep) { $wbOut.Write($wbBytes, (48 + $idx * 48), 48) }
    $trailing = 48 + $wbCount * 48
    $wbOut.Write($wbBytes, $trailing, $wbBytes.Length - $trailing)
    [IO.File]::WriteAllBytes((Join-Path $OutputRoot 'worldbuilder.manifest'), $wbOut.ToArray())
    Write-Host "  worldbuilder manifest filtered: $($keep.Count) art-only entries"
}

function Invoke-AssetBuilder {
    param([string]$StagedSource, [string]$OutputRoot)

    Copy-BaselineStreams -OutputRoot $OutputRoot
    Push-Location $SdkRoot
    try {
        & $builder `
            $StagedSource `
            "/od:$OutputRoot" `
            "/iod:$OutputRoot" `
            '/csc:false' `
            '/ls:true' `
            '/osh:false' `
            '/pc:true' `
            '/res:true' `
            '/slowclean:true' `
            '/ss:true' `
            '/art:.;.\Art' `
            '/audio:.;.\Audio' `
            '/data:.;.\SageXml_1.09'
        if ($LASTEXITCODE -ne 0) {
            throw "BinaryAssetBuilder failed with exit code $LASTEXITCODE"
        }
    }
    finally { Pop-Location }

    foreach ($fileName in $streamFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputRoot $fileName) -PathType Leaf)) {
            throw "BinaryAssetBuilder output missing: $fileName (in $OutputRoot)"
        }
    }
}

function Normalize-ManifestProvenance {
    param([string]$ManifestPath, [string]$StagedSource)

    $bytes = [IO.File]::ReadAllBytes($ManifestPath)
    $embeddedLen = [BitConverter]::ToUInt32($bytes, 44)
    $canonical = [Text.Encoding]::ASCII.GetBytes('Mod.xml')
    $canonicalLen = $canonical.Length + 1
    $pathPrefix = [Text.Encoding]::ASCII.GetBytes($StagedSource)
    if ($embeddedLen -lt $pathPrefix.Length + 1) {
        throw "Sources region ($embeddedLen bytes) too small to hold the staged path."
    }
    $prefixOk = $true
    for ($i = 0; $i -lt $pathPrefix.Length; $i++) {
        if ($bytes[$bytes.Length - $embeddedLen + $i] -ne $pathPrefix[$i]) { $prefixOk = $false; break }
    }
    if (-not $prefixOk -or $bytes[$bytes.Length - $embeddedLen + $pathPrefix.Length] -ne 0) {
        throw 'Sources region does not start with the staged path; refusing to normalize.'
    }

    $restLen = $embeddedLen - $pathPrefix.Length - 1
    $shift = $pathPrefix.Length + 1 - $canonicalLen
    $newLen = $bytes.Length - $embeddedLen + $canonicalLen + $restLen
    $out = [byte[]]::new($newLen)
    [Array]::Copy($bytes, 0, $out, 0, $bytes.Length - $embeddedLen)
    [Array]::Copy($canonical, 0, $out, $bytes.Length - $embeddedLen, $canonical.Length)
    $out[$bytes.Length - $embeddedLen + $canonical.Length] = 0
    [Array]::Copy($bytes, $bytes.Length - $restLen, $out, $bytes.Length - $embeddedLen + $canonicalLen, $restLen)

    # Entry @28 is a string offset relative to the sources-region start; entries referencing
    # the DATA: include strings after the path prefix must shift with the rewritten prefix.
    $entryCount = [BitConverter]::ToUInt32($bytes, 12)
    for ($i = 0; $i -lt $entryCount; $i++) {
        $o = 48 + $i * 48
        $ref = [BitConverter]::ToUInt32($bytes, $o + 28)
        if ($ref -eq 0) { continue }
        if ($ref -lt $pathPrefix.Length + 1) {
            throw "Entry $i references a string inside the staging path prefix (@28=$ref); unsupported."
        }
        [BitConverter]::GetBytes([uint32]($ref - $shift)).CopyTo($out, $o + 28)
    }

    [BitConverter]::GetBytes([uint32]($canonicalLen + $restLen)).CopyTo($out, 44)
    [IO.File]::WriteAllBytes($ManifestPath, $out)
    Write-Host "  provenance normalized: path $($pathPrefix.Length + 1) -> $canonicalLen bytes (sources region $embeddedLen -> $($canonicalLen + $restLen))"
}

function Add-ArsenalCloneSources {
    param([string]$StagedSource)

    $assetNs = 'uri:ea.com:eala:asset'
    $stagedXml = New-Object System.Xml.XmlDocument
    $stagedXml.PreserveWhitespace = $true
    $stagedXml.Load($StagedSource)
    $stagedNs = New-Object System.Xml.XmlNamespaceManager($stagedXml.NameTable)
    $stagedNs.AddNamespace('a', $assetNs)
    $includesNode = $stagedXml.SelectSingleNode('/a:AssetDeclaration/a:Includes', $stagedNs)
    if (-not $includesNode) { throw 'Mod.xml.source has no Includes node.' }
    $root = $stagedXml.DocumentElement
    $definesNode = $stagedXml.SelectSingleNode('/a:AssetDeclaration/a:Defines', $stagedNs)
    if (-not $definesNode) {
        $definesNode = $stagedXml.CreateElement('Defines', $assetNs)
        [void]$root.InsertAfter($definesNode, $includesNode)
    }

    for ($i = 0; $i -lt $arsenalCloneSpecs.Count; $i++) {
        $spec = $arsenalCloneSpecs[$i]
        $sourcePath = Join-Path (Join-Path $SdkRoot 'SageXml_1.09') $spec.Source
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Arsenal clone source missing: $sourcePath"
        }

        $cloneXml = New-Object System.Xml.XmlDocument
        $cloneXml.PreserveWhitespace = $true
        $cloneXml.Load($sourcePath)
        $cloneNs = New-Object System.Xml.XmlNamespaceManager($cloneXml.NameTable)
        $cloneNs.AddNamespace('a', $assetNs)
        $cloneNs.AddNamespace('xi', 'http://www.w3.org/2001/XInclude')
        foreach ($xinclude in @($cloneXml.SelectNodes('//xi:include', $cloneNs))) {
            $href = $xinclude.GetAttribute('href')
            if ($href -and $href -notmatch '^[A-Za-z]+:' -and -not [IO.Path]::IsPathRooted($href)) {
                $fullHref = [IO.Path]::GetFullPath((Join-Path (Split-Path $sourcePath) $href))
                $sageXmlRoot = [IO.Path]::GetFullPath((Join-Path $SdkRoot 'SageXml_1.09'))
                $relativeHref = [IO.Path]::GetRelativePath($sageXmlRoot, $fullHref).Replace('\', '/')
                $xinclude.SetAttribute('href', "DATA:$relativeHref")
            }
        }
        $object = $cloneXml.SelectSingleNode("//a:GameObject[@id='$($spec.SourceId)']", $cloneNs)
        if (-not $object) { throw "GameObject '$($spec.SourceId)' missing from $sourcePath" }
        foreach ($otherObject in @($cloneXml.SelectNodes('//a:GameObject', $cloneNs))) {
            if (-not [object]::ReferenceEquals($otherObject, $object)) {
                [void]$otherObject.ParentNode.RemoveChild($otherObject)
            }
        }

        $object.SetAttribute('id', $spec.Id)
        $object.SetAttribute('EditorName', $spec.Id)
        switch ($spec.Role) {
            'mcv' {
                $object.SetAttribute('CommandSet', 'Raya_ArsenalMCVCommandSet')
                $replacement = $object.SelectSingleNode('.//a:ReplaceSelfSpecialAbility/a:ReplacementTemplate', $cloneNs)
                if (-not $replacement) { throw "MCV replacement template missing from $sourcePath" }
                $replacement.InnerText = 'Raya_ArsenalConstructionYard'
            }
            'yard' {
                $object.SetAttribute('CommandSet', 'Raya_ArsenalConstructionYardCommandSet')
                foreach ($upgrade in @($object.SelectNodes('.//a:CommandSetUpgrade', $cloneNs))) {
                    $upgrade.SetAttribute('CommandSet', 'Raya_ArsenalConstructionYardCommandSet')
                }
                $replacement = $object.SelectSingleNode('.//a:ReplaceSelfSpecialAbility/a:ReplacementTemplate', $cloneNs)
                if (-not $replacement) { throw "Construction-yard replacement template missing from $sourcePath" }
                $replacement.InnerText = 'Raya_ArsenalMCV'
            }
            'factory' {
                $object.SetAttribute('CommandSet', 'Raya_ArsenalWarFactoryCommandSet')
                $object.SetAttribute('BuildTime', '30')
            }
            'tech' {
                $object.SetAttribute('BuildTime', '20')
                $object.SetAttribute('ProductionQueueType', 'MAIN_STRUCTURE')
                $object.SetAttribute('BuildPlacementTypeFlag', 'OTHER_STRUCTURE')
            }
            'defense' {
                $object.SetAttribute('CommandSet', 'EmptyCommandSet')
                $object.SetAttribute('BuildTime', '25')
                $object.SetAttribute('ProductionQueueType', 'MAIN_STRUCTURE')
                $object.SetAttribute('BuildPlacementTypeFlag', 'OTHER_STRUCTURE')
            }
            'civilian' {
                $object.SetAttribute('CommandSet', 'EmptyCommandSet')
                $object.SetAttribute('BuildTime', '15')
                $object.SetAttribute('ProductionQueueType', 'MAIN_STRUCTURE')
                $object.SetAttribute('BuildPlacementTypeFlag', 'OTHER_STRUCTURE')
            }
            default { throw "Unsupported arsenal clone role '$($spec.Role)'" }
        }

        foreach ($dependency in @($object.SelectNodes('./a:GameDependency', $cloneNs))) {
            [void]$object.RemoveChild($dependency)
        }
        foreach ($createObjectDie in @($object.SelectNodes('.//a:CreateObjectDie', $cloneNs))) {
            [void]$createObjectDie.ParentNode.RemoveChild($createObjectDie)
        }
        foreach ($creationList in @($cloneXml.SelectNodes('/a:AssetDeclaration/a:ObjectCreationList', $cloneNs))) {
            [void]$creationList.ParentNode.RemoveChild($creationList)
        }
        foreach ($collapseInclude in @($cloneXml.SelectNodes("/a:AssetDeclaration/a:Includes/a:Include[contains(translate(@source, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'collapse')]", $cloneNs))) {
            [void]$collapseInclude.ParentNode.RemoveChild($collapseInclude)
        }
        foreach ($destructionInclude in @($object.SelectNodes(".//xi:include[contains(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'destruction') or contains(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'suicide')]", $cloneNs))) {
            [void]$destructionInclude.ParentNode.RemoveChild($destructionInclude)
        }
        $buildCost = $object.SelectSingleNode('./a:ObjectResourceInfo/a:BuildCost', $cloneNs)
        if (-not $buildCost -and $spec.Role -in @('defense', 'civilian')) {
            $resourceInfo = $object.SelectSingleNode('./a:ObjectResourceInfo', $cloneNs)
            if (-not $resourceInfo) {
                $resourceInfo = $cloneXml.CreateElement('ObjectResourceInfo', $assetNs)
                [void]$object.AppendChild($resourceInfo)
            }
            $buildCost = $cloneXml.CreateElement('BuildCost', $assetNs)
            $buildCost.SetAttribute('Account', '=$ACCOUNT_ORE')
            [void]$resourceInfo.AppendChild($buildCost)
        }
        if ($buildCost -and $spec.Role -eq 'factory') { $buildCost.SetAttribute('Amount', '3000') }
        if ($buildCost -and $spec.Role -eq 'tech') { $buildCost.SetAttribute('Amount', '1000') }
        if ($buildCost -and $spec.Role -eq 'defense') { $buildCost.SetAttribute('Amount', '2500') }
        if ($buildCost -and $spec.Role -eq 'civilian') { $buildCost.SetAttribute('Amount', '500') }

        foreach ($cloneInclude in @($cloneXml.SelectNodes('/a:AssetDeclaration/a:Includes/a:Include', $cloneNs))) {
            [void]$includesNode.AppendChild($stagedXml.ImportNode($cloneInclude, $true))
        }
        foreach ($define in @($cloneXml.SelectNodes('/a:AssetDeclaration/a:Defines/a:Define', $cloneNs))) {
            $name = $define.GetAttribute('name')
            if (-not $definesNode.SelectSingleNode("a:Define[@name='$name']", $stagedNs)) {
                [void]$definesNode.AppendChild($stagedXml.ImportNode($define, $true))
            }
        }
        [void]$root.AppendChild($stagedXml.ImportNode($object, $true))
    }

    $stagedXml.Save($StagedSource)
}

function Invoke-HashFix {
    param([string]$ManifestPath)
    Push-Location (Join-Path $SdkRoot 'tools')
    try {
        $output = & $hashFix $ManifestPath 2>&1
        if ($LASTEXITCODE -ne 0 -or $output -match '^Error:') { throw "HashFix failed: $output" }
    }
    finally { Pop-Location }
}

function Invoke-ModAssetResolver {
    param([string]$RawRoot)

    $resolverEnv = Join-Path $WorkRoot 'resolver-env'
    $builtMods = Join-Path $resolverEnv 'BuiltMods'
    $tools = Join-Path $resolverEnv 'tools'
    $mod = Join-Path $resolverEnv 'mod'
    New-Item -ItemType Directory -Force -Path $builtMods, $tools, $mod | Out-Null

    foreach ($fileName in @('Audio.manifest', 'Global.manifest', 'Static.manifest')) {
        Copy-Item -LiteralPath (Join-Path $sdkBaseline $fileName) -Destination $builtMods -Force
    }
    Copy-Item -LiteralPath (Join-Path $RawRoot 'StringHashes.xml') -Destination $builtMods -Force
    Copy-Item -LiteralPath $resolver,(Join-Path $SdkRoot 'tools\ModAssetResolver_TypeHashes.txt'),(Join-Path $SdkRoot 'tools\ModAssetResolver_InstanceHashes.txt') -Destination $tools -Force

    if (-not (Test-Path -LiteralPath (Join-Path $builtMods 'worldbuilder.manifest'))) {
        # First run: ModAssetResolver extracts the WorldBuilder streams from the Steam
        # WBData.big via the WBData_12.big hardlink convention. Requires the RA3 1.13
        # (Steam) game install; resolved from the workspace config external ra3_113GameDir.
        $gameData = $null
        if ($env:RA3_113_GAME_DIR) {
            $gameData = Join-Path $env:RA3_113_GAME_DIR 'Data'
        }
        elseif ($workspaceConfig -and $workspaceConfig.external.ra3_113GameDir) {
            $gameData = Join-Path $workspaceConfig.external.ra3_113GameDir.value 'Data'
        }
        if (-not $gameData -or -not (Test-Path -LiteralPath (Join-Path $gameData 'WBData.big') -PathType Leaf)) {
            throw "WorldBuilder cache missing and no RA3 1.13 game Data dir found (RA3_113_GAME_DIR or workspace config ra3_113GameDir). Cannot run the first-run WBData extraction."
        }
        $wbLink = Join-Path $gameData 'WBData_12.big'
        if (-not (Test-Path -LiteralPath $wbLink)) {
            New-Item -ItemType HardLink -Path $wbLink -Target (Join-Path $gameData 'WBData.big') -ErrorAction Stop | Out-Null
        }
        foreach ($fileName in $streamFiles) {
            Copy-Item -LiteralPath (Join-Path $RawRoot $fileName) -Destination $mod -Force
        }
        Push-Location $tools
        try {
            $null = & $resolver (Join-Path $mod 'mod.manifest') 2>&1
            if ($LASTEXITCODE -ne 0) { throw 'ModAssetResolver first-run extraction failed.' }
        }
        finally { Pop-Location }
    }

    foreach ($fileName in $streamFiles) {
        Copy-Item -LiteralPath (Join-Path $RawRoot $fileName) -Destination $mod -Force
    }
    Push-Location $tools
    try {
        $output = & $resolver (Join-Path $mod 'mod.manifest') 2>&1
        if ($LASTEXITCODE -ne 0) { throw "ModAssetResolver failed: $output" }
    }
    finally { Pop-Location }
    return $mod
}

function Get-TemplateNamesFromXml {
    param([string]$Path)
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($Path)
    $nsm = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $nsm.AddNamespace('a', 'uri:ea.com:eala:asset')
    $names = @($xml.SelectNodes('//a:AttributeModifier', $nsm) | ForEach-Object { $_.GetAttribute('id') })
    $names += @($xml.SelectNodes('//a:GameObject', $nsm) | ForEach-Object { $_.GetAttribute('id') })
    $names += @($arsenalCloneSpecs | ForEach-Object { $_.Id })
    return $names
}

# ---- VerifyOnly mode -------------------------------------------------------

if ($VerifyOnly) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "asset-manifest.json missing: $manifestPath" }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $missing = $false
    $mismatch = $false
    $uprisingBlock = $manifest.PSObject.Properties['uprisingStreams'] ? $manifest.uprisingStreams : $null
    foreach ($block in @($manifest.streams, $uprisingBlock)) {
        if (-not $block) { continue }
        $sub = if ([object]::ReferenceEquals($block, $manifest.uprisingStreams)) { 'uprising' } else { '' }
        foreach ($key in @('manifest', 'bin', 'relo', 'imp')) {
            $file = Join-Path (Join-Path $AssetsRoot $sub) $block.$key.fileName
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
                Write-Host "MISSING  $(if ($sub) { "$sub\" })$($block.$key.fileName)" -ForegroundColor Yellow
                $missing = $true
                continue
            }
            $actual = Get-StreamSha256Upper $file
            if ($actual -ne $block.$key.sha256Upper) {
                Write-Host "MISMATCH $(if ($sub) { "$sub\" })$($block.$key.fileName): manifest $($block.$key.sha256Upper) != disk $actual" -ForegroundColor Red
                $mismatch = $true
            }
        }
    }
    if ($missing) { Write-Host 'Runtime asset streams missing. Run: pwsh -File scripts/build-runtime-assets.ps1' -ForegroundColor Yellow; exit 2 }
    if ($mismatch) { Write-Host 'Runtime asset stream hashes drifted from asset-manifest.json. Rebuild deliberately and re-baseline, or restore the generated streams.' -ForegroundColor Red; exit 1 }
    Write-Host 'Runtime asset streams match asset-manifest.json.' -ForegroundColor Green
    exit 0
}

# ---- Build mode ------------------------------------------------------------

foreach ($p in @($builder, $hashFix, $resolver, $converter, $sourceXml, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "Missing required input: $p" }
}
foreach ($p in @($truthDir, $sdkBaseline)) {
    if (-not (Test-Path -LiteralPath $p -PathType Container)) { throw "Missing required directory: $p" }
}

Write-Host 'Toolchain fingerprints (verified 2026-08-17, plan Phase 0):'
foreach ($tool in @($builder, $hashFix, $resolver, $converter)) {
    Write-Host "  $(Get-FileFingerprint $tool)"
}

# Keep resolver-env across runs (the WBData extraction is expensive); only the
# staging and raw outputs are per-run state.
foreach ($perRun in @((Join-Path $WorkRoot 'stage'), (Join-Path $WorkRoot 'raw-game6'), (Join-Path $WorkRoot 'raw-game7'))) {
    Remove-Item -Recurse -Force -LiteralPath $perRun -ErrorAction SilentlyContinue
}
$stageRoot = Join-Path $WorkRoot 'stage'
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

# ---- Game 6 (RA3 1.12) -----------------------------------------------------

Write-Host '==> Game 6 (RA3 1.12) bundle' -ForegroundColor Cyan
$staged6 = [IO.Path]::GetFullPath((Join-Path $stageRoot 'game6\Mod.xml'))
New-Item -ItemType Directory -Force -Path (Split-Path $staged6) | Out-Null
Copy-Item -LiteralPath $sourceXml -Destination $staged6 -Force
Add-ArsenalCloneSources -StagedSource $staged6

$raw6 = Join-Path $WorkRoot 'raw-game6'
New-Item -ItemType Directory -Force -Path $raw6 | Out-Null
Write-FilteredWorldbuilderManifest -OutputRoot $raw6
Invoke-AssetBuilder -StagedSource $staged6 -OutputRoot $raw6
Normalize-ManifestProvenance -ManifestPath (Join-Path $raw6 'mod.manifest') -StagedSource $staged6
Invoke-HashFix -ManifestPath (Join-Path $raw6 'mod.manifest')
$rawCount6 = Assert-AssetGates -Bytes ([IO.File]::ReadAllBytes((Join-Path $raw6 'mod.manifest'))) -ExpectedKeys $expectedGameObjectKeys -Label 'game6-raw'
Write-Host "  raw asset count: $rawCount6"

$merged6 = Invoke-ModAssetResolver -RawRoot $raw6
$mergedBytes = [IO.File]::ReadAllBytes((Join-Path $merged6 'mod.manifest'))
$finalCount6 = Assert-AssetGates -Bytes $mergedBytes -ExpectedKeys $expectedGameObjectKeys -Label 'game6-final'
Write-Host "  final asset count: $finalCount6"
$builtManifestEntries = (Get-ManifestAssetKeys $mergedBytes).Keys

foreach ($fileName in $streamFiles) {
    Copy-Item -LiteralPath (Join-Path $merged6 $fileName) -Destination (Join-Path $AssetsRoot $fileName) -Force
}

# ---- Game 7 (Uprising) -----------------------------------------------------

Write-Host '==> Game 7 (Uprising) bundle' -ForegroundColor Cyan
$xml = New-Object System.Xml.XmlDocument
$xml.Load($sourceXml)
$nsm = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$nsm.AddNamespace('a', 'uri:ea.com:eala:asset')
$stripped = 0
foreach ($assetType in @('GameObject', 'LogicCommand', 'LogicCommandSet')) {
    foreach ($node in @($xml.SelectNodes("//a:$assetType", $nsm))) {
        [void]$node.ParentNode.RemoveChild($node)
        $stripped++
    }
}
foreach ($inc in @($xml.SelectNodes('//a:Include', $nsm))) {
    if (@('DATA:static.xml', 'DATA:global.xml', 'DATA:audio.xml') -notcontains $inc.GetAttribute('source')) {
        [void]$inc.ParentNode.RemoveChild($inc)
    }
}
$staged7 = [IO.Path]::GetFullPath((Join-Path $stageRoot 'game7\Mod.xml'))
New-Item -ItemType Directory -Force -Path (Split-Path $staged7) | Out-Null
$xml.Save($staged7)
Write-Host "  stripped $stripped GameObject/LogicCommand node(s) + non-data includes"

$raw7 = Join-Path $WorkRoot 'raw-game7'
New-Item -ItemType Directory -Force -Path $raw7 | Out-Null
Invoke-AssetBuilder -StagedSource $staged7 -OutputRoot $raw7
Normalize-ManifestProvenance -ManifestPath (Join-Path $raw7 'mod.manifest') -StagedSource $staged7
Invoke-HashFix -ManifestPath (Join-Path $raw7 'mod.manifest')
$bytes7 = [IO.File]::ReadAllBytes((Join-Path $raw7 'mod.manifest'))
$count7 = Assert-AssetGates -Bytes $bytes7 -ExpectedKeys @() -Label 'game7'
if ($count7 -ne $game7ExpectedCount) {
    throw "Game 7 bundle must carry exactly $game7ExpectedCount AttributeModifier entries, got $count7."
}
$sessionHex = ([BitConverter]::ToString($bytes7[4..7]) -replace '-', '')
Write-Host "  v7 session id: $sessionHex"

$game7Root = Join-Path $AssetsRoot 'uprising'
New-Item -ItemType Directory -Force -Path $game7Root | Out-Null
$v7Manifest = Join-Path $raw7 'mod.manifest.v7'
& $converter convert (Join-Path $raw7 'mod.manifest') $v7Manifest --truth $truthDir | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'RA3-Uprising-Converter convert failed.' }
foreach ($kind in @('bin', 'imp', 'relo')) {
    & $converter patch-headers $kind (Join-Path $raw7 "mod.$kind") $sessionHex (Join-Path $game7Root "mod.$kind") | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "RA3-Uprising-Converter patch-headers $kind failed." }
}
Copy-Item -LiteralPath $v7Manifest -Destination (Join-Path $game7Root 'mod.manifest') -Force

# ---- asset-manifest.json recompute -----------------------------------------

Write-Host '==> asset-manifest.json recompute' -ForegroundColor Cyan
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

$xmlTemplateNames = @(Get-TemplateNamesFromXml -Path $sourceXml)
$tableNames = @($manifest.templates | ForEach-Object { $_.name })
$added = @($xmlTemplateNames | Where-Object { $tableNames -notcontains $_ })
$removed = @($tableNames | Where-Object { $xmlTemplateNames -notcontains $_ })
if ($added.Count -or $removed.Count) {
    foreach ($name in $added) { Write-Host "  template added in Mod.xml.source but missing from manifest table: $name" -ForegroundColor Yellow }
    foreach ($name in $removed) { Write-Host "  template removed from Mod.xml.source but still in manifest table: $name" -ForegroundColor Yellow }
    $orphans = @($builtManifestEntries | Where-Object {
        $parts = $_ -split ':'
        $flat = ('0x' + $parts[0]) + ':' + ('0x' + $parts[1])
        -not (@($manifest.templates | ForEach-Object { "$($_.typeId):$($_.instanceId)" }) -contains $flat)
    })
    if ($orphans.Count) {
        Write-Host '  new (typeId:instanceId) pairs present in the built Game 6 manifest but not in the table (fill metadata manually):' -ForegroundColor Yellow
        foreach ($orphan in $orphans) { Write-Host "    $orphan" -ForegroundColor Yellow }
    }
    throw ('Template table out of sync with Mod.xml.source (added: {0}, removed: {1}). ' +
        'Update asset-manifest.json templates manually (metadata is semantic, not derivable), bump packageVersion deliberately, then re-run.' -f $added.Count, $removed.Count)
}

foreach ($template in $manifest.templates) {
    $flat = '{0}:{1}' -f ($template.typeId -replace '^0x', ''), ($template.instanceId -replace '^0x', '')
    if ($builtManifestEntries -notcontains $flat) {
        throw "Template '$($template.name)' ($flat) not found in the built Game 6 manifest."
    }
}

foreach ($key in @('manifest', 'bin', 'relo', 'imp')) {
    $manifest.streams.$key.sha256Upper = Get-StreamSha256Upper (Join-Path $AssetsRoot $manifest.streams.$key.fileName)
}
if (-not $manifest.PSObject.Properties['uprisingStreams']) {
    $manifest | Add-Member -NotePropertyName uprisingStreams -NotePropertyValue ([pscustomobject]@{
        manifest = [pscustomobject]@{ fileName = 'mod.manifest'; sha256Upper = '' }
        bin      = [pscustomobject]@{ fileName = 'mod.bin'; sha256Upper = '' }
        relo     = [pscustomobject]@{ fileName = 'mod.relo'; sha256Upper = '' }
        imp      = [pscustomobject]@{ fileName = 'mod.imp'; sha256Upper = '' }
    })
}
foreach ($key in @('manifest', 'bin', 'relo', 'imp')) {
    $manifest.uprisingStreams.$key.sha256Upper = Get-StreamSha256Upper (Join-Path $game7Root $manifest.uprisingStreams.$key.fileName)
}

$manifest | ConvertTo-Json -Depth 64 | Set-Content -NoNewline -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "  stream hashes updated (packageVersion kept at '$($manifest.packageVersion)')."
Write-Host 'Done. git status should show asset-manifest.json as the only tracked text change.'
