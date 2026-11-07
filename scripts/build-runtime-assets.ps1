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

$campaignCloneSpecs = @(
    [pscustomobject]@{ Source = 'Japan\Units_SinglePlayerCampaign\JapanMechaKing.xml'; SourceId = 'JapanMechaKing'; Id = 'Raya_MechaKing' },
    [pscustomobject]@{ Source = 'Japan\Units_SinglePlayerCampaign\JapanEmperorMecha.xml'; SourceId = 'JapanEmperorMecha'; Id = 'Raya_EmperorMecha' },
    [pscustomobject]@{ Source = 'Japan\Units_SinglePlayerCampaign\JapanEmperor.xml'; SourceId = 'JapanEmperor'; Id = 'Raya_Emperor' },
    [pscustomobject]@{ Source = 'Japan\Units_SinglePlayerCampaign\A04_ExplodingTengu.xml'; SourceId = 'A04_ExplodingTengu'; Id = 'Raya_ExplodingTengu' },
    [pscustomobject]@{ Source = 'Allied\Units_Campaign\CAMP_A08_SpecialMirageTank.xml'; SourceId = 'A08_SpecialMirageTank'; Id = 'Raya_SpecialMirageTank' },
    [pscustomobject]@{ Source = 'Allied\Units_Campaign\AlliedLimo1.xml'; SourceId = 'AlliedLimo1'; Id = 'Raya_PresidentialLimo' })

# These WorldBuilder-only W3X files are empty SDK placeholders. Their compiled art exists only
# in the WorldBuilder streams, so XML references and the corresponding precompiled entries must
# be renamed together. IDs are frozen FastHash values for the source and Raya-owned names.
$renamedArtAssets = @(
    [pscustomobject]@{ OldId = [Convert]::ToUInt32('88F2BA53', 16); NewId = [Convert]::ToUInt32('2ED0096B', 16); OldName = 'BB_EuropeCoastalGun'; Name = 'RayaArt_ArsenalEuropeCoastalGun' },
    [pscustomobject]@{ OldId = [Convert]::ToUInt32('08492FF7', 16); NewId = [Convert]::ToUInt32('29488A96', 16); OldName = 'KR_ArtilleryDome'; Name = 'RayaArt_ArsenalArtilleryDome' },
    [pscustomobject]@{ OldId = [Convert]::ToUInt32('D23C0CEB', 16); NewId = [Convert]::ToUInt32('22FE39A9', 16); OldName = 'FI_FloatingFortressMainGun'; Name = 'RayaArt_ArsenalJapanTriCannon' },
    [pscustomobject]@{ OldId = [Convert]::ToUInt32('A403C724', 16); NewId = [Convert]::ToUInt32('F5180984', 16); OldName = 'CapeCod_House01'; Name = 'RayaArt_ArsenalCapeCodHouse' })

$weaponCloneSpecs = @(
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'AlliedAntiVehicleVehicleTech3PrismCannon'; OldId = '0E868C9B'; NewId = '3BD41571' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'AlliedAntiVehicleVehicleTech3PrismCannon_Veteran'; OldId = '660AB9BA'; NewId = '598D2FE0' },
    [pscustomobject]@{ Source = 'BaseObjects\BaseExplodingPropVehicle.xml'; Name = 'BaseExplodingPropVehicleWeapon'; OldId = 'D967BB85'; NewId = '615BAF7F' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'CatalystBuildingDeathWeapon'; OldId = 'BB2D8E68'; NewId = '22ADF716' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'CatalystInfantryDeathWeapon'; OldId = '87536CE9'; NewId = '0143654E' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'DefensiveTechStructureWeaponWarhead'; OldId = '5047D30C'; NewId = '6CBA5033' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'DefensiveTechStructureWeapon'; OldId = '485AC85D'; NewId = '093264AE' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'EnhancedKamikazeDeathPlayerPowerWeapon'; OldId = 'A6FB4093'; NewId = '547D1344' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'JapanEmperorMechaBeamWeapon'; OldId = '169FA19F'; NewId = 'CAA806D7' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'JapanEmperorMechaRushAttackWeapon'; OldId = '873C0B3F'; NewId = 'CA9B4813' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'JapanFloatingFortressMainGun'; OldId = '55D5F81E'; NewId = '31C3734C' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'JapanMechaKingKatana'; OldId = '1AF73043'; NewId = 'C7D57FBD' },
    [pscustomobject]@{ Source = 'GlobalData\Weapon.xml'; Name = 'JapanMechaKingOmegaShockwave'; OldId = '8D352155'; NewId = '90B61DD1' })

# BAB compiles explicit instance includes even when the owned clones no longer use them.
# Keep this deny list exact: each entry must be both unreferenced by retained assets and
# removed from the final package so map-owned definitions cannot collide during teardown.
$compilationOnlyAssetSpecs = @(
    @($weaponCloneSpecs | ForEach-Object {
        [pscustomobject]@{ TypeId = '94D4D96E'; InstanceId = $_.OldId; Name = $_.Name }
    })
    [pscustomobject]@{ TypeId = '942FFF2D'; InstanceId = '5B1630A7'; Name = 'YU_HotelDebris' }
)

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

function Assert-OwnedSemanticAssetIds {
    param([string]$ManifestPath, [string]$StringHashesPath, [string]$Label)

    [xml]$hashXml = Get-Content -LiteralPath $StringHashesPath
    $hashNs = New-Object System.Xml.XmlNamespaceManager($hashXml.NameTable)
    $hashNs.AddNamespace('a', 'uri:ea.com:eala:asset')
    $names = @{}
    foreach ($entry in $hashXml.SelectNodes('//a:StringHashTable[@StringHashBin="INSTANCEID"]/a:StringAndHash', $hashNs)) {
        $names[[uint32]$entry.Hash] = $entry.Text
    }

    $allowedPrefixesByType = @{
        '942FFF2D' = @('Raya_')          # GameObject
        '94D4D96E' = @('Raya_')          # WeaponTemplate
        'E86E4D61' = @('Raya_')          # ObjectCreationList
        '86682E78' = @('Raya_')          # FXList
        'C5E07887' = @('Raya_', 'Atlas_') # AttributeModifier
        '7D464170' = @('Raya_', 'Command_Raya') # LogicCommand
        'EC066D65' = @('Raya_')          # LogicCommandSet
    }
    $bytes = [IO.File]::ReadAllBytes($ManifestPath)
    $count = [BitConverter]::ToUInt32($bytes, 12)
    for ($i = 0; $i -lt $count; $i++) {
        $offset = 48 + $i * 48
        $typeId = [BitConverter]::ToUInt32($bytes, $offset)
        $typeKey = '{0:X8}' -f $typeId
        if (-not $allowedPrefixesByType.ContainsKey($typeKey)) { continue }
        $instanceId = [BitConverter]::ToUInt32($bytes, $offset + 4)
        $name = $names[$instanceId]
        if (-not $name) {
            throw ('[{0}] Owned semantic asset name is absent from StringHashes.xml: {1:X8}:{2:X8}' -f $Label, $typeId, $instanceId)
        }
        $owned = $false
        foreach ($prefix in $allowedPrefixesByType[$typeKey]) {
            if ($name.StartsWith($prefix, [StringComparison]::Ordinal)) { $owned = $true; break }
        }
        if (-not $owned) {
            throw ('[{0}] Non-owned semantic asset leaked into runtime package: {1:X8}:{2:X8} {3}' -f $Label, $typeId, $instanceId, $name)
        }
    }
}

function Assert-RenamedArtAssets {
    param([byte[]]$ManifestBytes, [string]$Label)

    $info = Get-ManifestAssetKeys $ManifestBytes
    $artTypes = @('F0F08712', 'C2B1A262', '61D7EA40', '21E727DA', '2448AE30', 'E3181C04')
    foreach ($rename in $renamedArtAssets) {
        $newContainer = 'F0F08712:{0:X8}' -f $rename.NewId
        if ($info.Keys -notcontains $newContainer) {
            throw "[$Label] Renamed art container missing from runtime package: $newContainer $($rename.Name)"
        }
        foreach ($typeId in $artTypes) {
            $oldKey = '{0}:{1:X8}' -f $typeId, $rename.OldId
            if ($info.Keys -contains $oldKey) {
                throw "[$Label] Original art asset leaked into runtime package: $oldKey $($rename.OldName)"
            }
        }
    }
}

function Get-ReferencedManifestRecords {
    param([byte[]]$ManifestBytes)

    $assetCount = [BitConverter]::ToUInt32($ManifestBytes, 12)
    $assetReferenceSize = [BitConverter]::ToUInt32($ManifestBytes, 32)
    $referencedManifestSize = [BitConverter]::ToUInt32($ManifestBytes, 36)
    $start = 48 + $assetCount * 48 + $assetReferenceSize
    $end = $start + $referencedManifestSize
    if ($end -gt $ManifestBytes.Length) {
        throw "Referenced-manifest region exceeds manifest length ($end > $($ManifestBytes.Length))."
    }

    $cursor = $start
    while ($cursor -lt $end) {
        $recordStart = $cursor
        $kind = $ManifestBytes[$cursor]
        ++$cursor
        $pathStart = $cursor
        while ($cursor -lt $end -and $ManifestBytes[$cursor] -ne 0) { ++$cursor }
        if ($cursor -ge $end) { throw 'Referenced-manifest path is not null-terminated.' }
        $path = [Text.Encoding]::ASCII.GetString($ManifestBytes, $pathStart, $cursor - $pathStart)
        ++$cursor
        [pscustomobject]@{
            Kind = [byte]$kind
            Path = $path
            Offset = $recordStart
            Length = $cursor - $recordStart
        }
    }
}

function Remove-BuildOnlyManifestReference {
    param([string]$ManifestPath, [string]$ReferencePath)

    $bytes = [IO.File]::ReadAllBytes($ManifestPath)
    $records = @(Get-ReferencedManifestRecords -ManifestBytes $bytes)
    $removed = @($records | Where-Object {
        $_.Kind -eq 1 -and $_.Path.Equals($ReferencePath, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($removed.Count -ne 1) {
        throw "Expected exactly one build-only reference '$ReferencePath', found $($removed.Count)."
    }

    $assetCount = [BitConverter]::ToUInt32($bytes, 12)
    $assetReferenceSize = [BitConverter]::ToUInt32($bytes, 32)
    $oldReferencedManifestSize = [BitConverter]::ToUInt32($bytes, 36)
    $regionStart = 48 + $assetCount * 48 + $assetReferenceSize
    $regionEnd = $regionStart + $oldReferencedManifestSize
    $kept = New-Object System.IO.MemoryStream
    foreach ($record in $records) {
        if ($record -eq $removed[0]) { continue }
        $kept.Write($bytes, $record.Offset, $record.Length)
    }

    $keptBytes = $kept.ToArray()
    [BitConverter]::GetBytes([uint32]$keptBytes.Length).CopyTo($bytes, 36)
    $output = New-Object System.IO.MemoryStream
    $output.Write($bytes, 0, $regionStart)
    $output.Write($keptBytes, 0, $keptBytes.Length)
    $output.Write($bytes, $regionEnd, $bytes.Length - $regionEnd)
    [IO.File]::WriteAllBytes($ManifestPath, $output.ToArray())
    Write-Host "  removed build-only manifest reference: $ReferencePath"
}

function Assert-RuntimeManifestReferences {
    param([byte[]]$ManifestBytes, [string]$Label)

    $actual = @(Get-ReferencedManifestRecords -ManifestBytes $ManifestBytes)
    $expected = @('static.manifest', 'global.manifest', 'audio.manifest')
    if ($actual.Count -ne $expected.Count) {
        throw "[$Label] Expected $($expected.Count) runtime manifest references, got $($actual.Count): $($actual.Path -join ', ')"
    }
    for ($i = 0; $i -lt $expected.Count; $i++) {
        if ($actual[$i].Kind -ne 1 -or -not $actual[$i].Path.Equals($expected[$i], [StringComparison]::OrdinalIgnoreCase)) {
            throw "[$Label] Runtime manifest reference[$i] must be kind=1 '$($expected[$i])', got kind=$($actual[$i].Kind) '$($actual[$i].Path)'."
        }
    }
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

function Convert-RelativeXIncludesToDataPaths {
    param([System.Xml.XmlDocument]$Xml, [string]$SourcePath, [System.Xml.XmlNamespaceManager]$Namespaces)

    foreach ($xinclude in @($Xml.SelectNodes('//xi:include', $Namespaces))) {
        $href = $xinclude.GetAttribute('href')
        if ($href -and $href -notmatch '^[A-Za-z]+:' -and -not [IO.Path]::IsPathRooted($href)) {
            $fullHref = [IO.Path]::GetFullPath((Join-Path (Split-Path $SourcePath) $href))
            $sageXmlRoot = [IO.Path]::GetFullPath((Join-Path $SdkRoot 'SageXml_1.09'))
            $relativeHref = [IO.Path]::GetRelativePath($sageXmlRoot, $fullHref).Replace('\', '/')
            $xinclude.SetAttribute('href', "DATA:$relativeHref")
        }
    }
}

function Rewrite-ClonedAssetReferences {
    param([System.Xml.XmlNode]$Node, [hashtable]$IdMap)

    if ($Node.Attributes) {
        foreach ($attribute in @($Node.Attributes)) {
            if ($attribute.LocalName -notin @('id', 'inheritFrom', 'Template', 'ProjectileTemplate', 'WarheadTemplate', 'AttributeModifierName', 'FX', 'FireFX', 'GroundHitFX', 'CreationList')) { continue }
            foreach ($oldId in $IdMap.Keys) {
                $attribute.Value = [regex]::Replace(
                    $attribute.Value,
                    '(?<![A-Za-z0-9_])' + [regex]::Escape($oldId) + '(?![A-Za-z0-9_])',
                    [string]$IdMap[$oldId])
            }
        }
    }
    if ($Node.NodeType -in @([System.Xml.XmlNodeType]::Text, [System.Xml.XmlNodeType]::CDATA)) {
        if ($Node.ParentNode.LocalName -notin @('Object', 'ReplacementTemplate', 'CreateObject')) { return }
        foreach ($oldId in $IdMap.Keys) {
            $Node.Value = [regex]::Replace(
                $Node.Value,
                '(?<![A-Za-z0-9_])' + [regex]::Escape($oldId) + '(?![A-Za-z0-9_])',
                [string]$IdMap[$oldId])
        }
    }
    foreach ($child in @($Node.ChildNodes)) { Rewrite-ClonedAssetReferences -Node $child -IdMap $IdMap }
}

function Rewrite-ClonedArtReferences {
    param([System.Xml.XmlNode]$Node, [string]$OldName, [string]$NewName)

    if ($Node.Attributes) {
        foreach ($attribute in @($Node.Attributes)) {
            $isArtInclude = $Node.LocalName -eq 'Include' -and $attribute.LocalName -eq 'source' -and $attribute.Value.StartsWith('ART:', [StringComparison]::OrdinalIgnoreCase)
            $isModelName = $Node.LocalName -eq 'Model' -and $attribute.LocalName -eq 'Name'
            $isAnimationName = $attribute.LocalName -eq 'AnimationName'
            if ($isArtInclude -or $isModelName -or $isAnimationName) {
                $attribute.Value = [regex]::Replace(
                    $attribute.Value,
                    '(?<![A-Za-z0-9_])' + [regex]::Escape($OldName) + '(?![A-Za-z0-9_])',
                    $NewName)
            }
        }
    }
    foreach ($child in @($Node.ChildNodes)) { Rewrite-ClonedArtReferences -Node $child -OldName $OldName -NewName $NewName }
}

function Remove-JoinActions {
    param([System.Xml.XmlNode]$Node)
    if ($Node.Attributes) {
        foreach ($attribute in @($Node.Attributes)) {
            if ($attribute.LocalName -eq 'joinAction' -and $attribute.NamespaceURI -eq 'uri:ea.com:eala:asset:instance') {
                [void]$Node.Attributes.Remove($attribute)
            }
        }
    }
    foreach ($child in @($Node.ChildNodes)) { Remove-JoinActions -Node $child }
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
        Convert-RelativeXIncludesToDataPaths -Xml $cloneXml -SourcePath $sourcePath -Namespaces $cloneNs
        $object = $cloneXml.SelectSingleNode("//a:GameObject[@id='$($spec.SourceId)']", $cloneNs)
        if (-not $object) { throw "GameObject '$($spec.SourceId)' missing from $sourcePath" }
        $idMap = @{}
        $idMap[$spec.SourceId] = $spec.Id
        foreach ($asset in @($cloneXml.SelectNodes('/a:AssetDeclaration/*[@id]', $cloneNs))) {
            if ([object]::ReferenceEquals($asset, $object) -or $asset.LocalName -eq 'ObjectCreationList') { continue }
            $idMap[$asset.GetAttribute('id')] = 'Raya_' + $asset.GetAttribute('id')
        }
        Rewrite-ClonedAssetReferences -Node $cloneXml.DocumentElement -IdMap $idMap
        $artRename = $renamedArtAssets | Where-Object OldName -eq $spec.SourceId | Select-Object -First 1
        if ($artRename) {
            Rewrite-ClonedArtReferences -Node $cloneXml.DocumentElement -OldName $artRename.OldName -NewName $artRename.Name
        }
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
        foreach ($dependencyAsset in @($cloneXml.SelectNodes('/a:AssetDeclaration/*[@id]', $cloneNs))) {
            if ([object]::ReferenceEquals($dependencyAsset, $object) -or $dependencyAsset.LocalName -eq 'ObjectCreationList') { continue }
            [void]$root.AppendChild($stagedXml.ImportNode($dependencyAsset, $true))
        }
    }

    $stagedXml.Save($StagedSource)
}

function Add-CampaignCloneSources {
    param([string]$StagedSource)

    $assetNs = 'uri:ea.com:eala:asset'
    $stagedXml = New-Object System.Xml.XmlDocument
    $stagedXml.PreserveWhitespace = $true
    $stagedXml.Load($StagedSource)
    $stagedNs = New-Object System.Xml.XmlNamespaceManager($stagedXml.NameTable)
    $stagedNs.AddNamespace('a', $assetNs)
    $stagedNs.AddNamespace('xai', 'uri:ea.com:eala:asset:instance')
    $includesNode = $stagedXml.SelectSingleNode('/a:AssetDeclaration/a:Includes', $stagedNs)
    $root = $stagedXml.DocumentElement
    $definesNode = $stagedXml.SelectSingleNode('/a:AssetDeclaration/a:Defines', $stagedNs)
    if (-not $definesNode) {
        $definesNode = $stagedXml.CreateElement('Defines', $assetNs)
        [void]$root.InsertAfter($definesNode, $includesNode)
    }

    foreach ($spec in $campaignCloneSpecs) {
        $sourcePath = Join-Path (Join-Path $SdkRoot 'SageXml_1.09') $spec.Source
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Campaign clone source missing: $sourcePath" }

        $cloneXml = New-Object System.Xml.XmlDocument
        $cloneXml.PreserveWhitespace = $true
        $cloneXml.Load($sourcePath)
        $cloneNs = New-Object System.Xml.XmlNamespaceManager($cloneXml.NameTable)
        $cloneNs.AddNamespace('a', $assetNs)
        $cloneNs.AddNamespace('xi', 'http://www.w3.org/2001/XInclude')
        Convert-RelativeXIncludesToDataPaths -Xml $cloneXml -SourcePath $sourcePath -Namespaces $cloneNs

        $object = $cloneXml.SelectSingleNode("/a:AssetDeclaration/a:GameObject[@id='$($spec.SourceId)']", $cloneNs)
        $override = $stagedXml.SelectSingleNode("/a:AssetDeclaration/a:GameObject[@id='$($spec.Id)']", $stagedNs)
        if (-not $object -or -not $override) { throw "Campaign clone '$($spec.SourceId)' or override '$($spec.Id)' is missing." }

        foreach ($createObjectDie in @($object.SelectNodes('.//a:CreateObjectDie', $cloneNs))) {
            [void]$createObjectDie.ParentNode.RemoveChild($createObjectDie)
        }
        foreach ($destructionInclude in @($object.SelectNodes(".//xi:include[contains(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'destruction') or contains(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'collapse') or contains(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'suicide')]", $cloneNs))) {
            [void]$destructionInclude.ParentNode.RemoveChild($destructionInclude)
        }

        $idMap = @{}
        $idMap[$spec.SourceId] = $spec.Id
        foreach ($asset in @($cloneXml.SelectNodes('/a:AssetDeclaration/*[@id]', $cloneNs))) {
            if ([object]::ReferenceEquals($asset, $object) -or $asset.LocalName -eq 'ObjectCreationList') { continue }
            $idMap[$asset.GetAttribute('id')] = 'Raya_' + $asset.GetAttribute('id')
        }
        Rewrite-ClonedAssetReferences -Node $cloneXml.DocumentElement -IdMap $idMap

        foreach ($attribute in @($override.Attributes)) {
            if ($attribute.LocalName -notin @('id', 'inheritFrom') -and $attribute.NamespaceURI -ne 'uri:ea.com:eala:asset:instance') {
                $object.SetAttribute($attribute.LocalName, $attribute.Value)
            }
        }
        foreach ($overrideChild in @($override.ChildNodes | Where-Object NodeType -eq ([System.Xml.XmlNodeType]::Element))) {
            $existing = @($object.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and $_.LocalName -eq $overrideChild.LocalName })
            if ($overrideChild.GetAttribute('joinAction', 'uri:ea.com:eala:asset:instance') -eq 'Replace') {
                foreach ($node in $existing) { [void]$object.RemoveChild($node) }
            }
            if ($overrideChild.LocalName -eq 'GameDependency' -and -not $overrideChild.HasChildNodes -and $overrideChild.Attributes.Count -le 1) { continue }
            $replacement = $cloneXml.ImportNode($overrideChild, $true)
            Remove-JoinActions -Node $replacement
            [void]$object.AppendChild($replacement)
        }

        foreach ($cloneInclude in @($cloneXml.SelectNodes('/a:AssetDeclaration/a:Includes/a:Include', $cloneNs))) {
            [void]$includesNode.AppendChild($stagedXml.ImportNode($cloneInclude, $true))
        }
        foreach ($define in @($cloneXml.SelectNodes('/a:AssetDeclaration/a:Defines/a:Define', $cloneNs))) {
            $name = $define.GetAttribute('name')
            if (-not $definesNode.SelectSingleNode("a:Define[@name='$name']", $stagedNs)) {
                [void]$definesNode.AppendChild($stagedXml.ImportNode($define, $true))
            }
        }
        [void]$root.ReplaceChild($stagedXml.ImportNode($object, $true), $override)
        foreach ($dependencyAsset in @($cloneXml.SelectNodes('/a:AssetDeclaration/*[@id]', $cloneNs))) {
            if ([object]::ReferenceEquals($dependencyAsset, $object) -or $dependencyAsset.LocalName -eq 'ObjectCreationList') { continue }
            [void]$root.AppendChild($stagedXml.ImportNode($dependencyAsset, $true))
        }

        $normalizedSource = $spec.Source.Replace('\', '/').ToLowerInvariant()
        foreach ($include in @($includesNode.SelectNodes('a:Include[@type="instance"]', $stagedNs))) {
            $source = $include.GetAttribute('source').Replace('\', '/').ToLowerInvariant()
            if ($source.EndsWith($normalizedSource)) { [void]$includesNode.RemoveChild($include) }
        }
    }

    $stagedXml.Save($StagedSource)
}

function Add-WeaponCloneSources {
    param([string]$StagedSource)

    $assetNs = 'uri:ea.com:eala:asset'
    $stagedXml = New-Object System.Xml.XmlDocument
    $stagedXml.PreserveWhitespace = $true
    $stagedXml.Load($StagedSource)
    $stagedNs = New-Object System.Xml.XmlNamespaceManager($stagedXml.NameTable)
    $stagedNs.AddNamespace('a', $assetNs)
    $root = $stagedXml.DocumentElement
    $idMap = @{}
    foreach ($spec in $weaponCloneSpecs) { $idMap[$spec.Name] = 'Raya_' + $spec.Name }

    $loadedSources = @{}
    foreach ($spec in $weaponCloneSpecs) {
        if (-not $loadedSources.ContainsKey($spec.Source)) {
            $sourcePath = Join-Path (Join-Path $SdkRoot 'SageXml_1.09') $spec.Source
            $sourceXml = New-Object System.Xml.XmlDocument
            $sourceXml.PreserveWhitespace = $true
            $sourceXml.Load($sourcePath)
            $sourceNs = New-Object System.Xml.XmlNamespaceManager($sourceXml.NameTable)
            $sourceNs.AddNamespace('a', $assetNs)
            $loadedSources[$spec.Source] = [pscustomobject]@{ Xml = $sourceXml; Namespaces = $sourceNs }
        }
        $source = $loadedSources[$spec.Source]
        $weapon = $source.Xml.SelectSingleNode("/a:AssetDeclaration/a:WeaponTemplate[@id='$($spec.Name)']", $source.Namespaces)
        if (-not $weapon) { throw "WeaponTemplate '$($spec.Name)' missing from $($spec.Source)." }
        $clone = $stagedXml.ImportNode($weapon, $true)
        Rewrite-ClonedAssetReferences -Node $clone -IdMap $idMap
        [void]$root.AppendChild($clone)
    }

    $stagedXml.Save($StagedSource)
}

function Redirect-ClonedWeaponReferences {
    param([string]$ManifestPath)

    $bytes = [IO.File]::ReadAllBytes($ManifestPath)
    $count = [BitConverter]::ToUInt32($bytes, 12)
    $referenceBytes = [BitConverter]::ToUInt32($bytes, 32)
    $referencesStart = 48 + $count * 48
    $redirects = @{}
    foreach ($spec in $weaponCloneSpecs) {
        $redirects[[Convert]::ToUInt32($spec.OldId, 16)] = [Convert]::ToUInt32($spec.NewId, 16)
    }
    $patched = 0
    for ($offset = $referencesStart; $offset -lt $referencesStart + $referenceBytes; $offset += 8) {
        $instanceId = [BitConverter]::ToUInt32($bytes, $offset + 4)
        if ($redirects.ContainsKey($instanceId)) {
            [BitConverter]::GetBytes([uint32]$redirects[$instanceId]).CopyTo($bytes, $offset + 4)
            ++$patched
        }
    }
    if ($patched -eq 0) { throw 'No compiled weapon references matched the Raya redirect table.' }
    [IO.File]::WriteAllBytes($ManifestPath, $bytes)
    Write-Host "  redirected $patched compiled weapon references to Raya-owned clones"
}

function Remove-CompilationOnlyAssets {
    param([string]$OutputRoot)

    $manifestPath = Join-Path $OutputRoot 'mod.manifest'
    $manifest = [IO.File]::ReadAllBytes($manifestPath)
    $count = [BitConverter]::ToUInt32($manifest, 12)
    $removeKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($spec in $compilationOnlyAssetSpecs) {
        [void]$removeKeys.Add(('{0}:{1}' -f $spec.TypeId, $spec.InstanceId))
    }

    $referencesStart = 48 + $count * 48
    for ($i = 0; $i -lt $count; $i++) {
        $entryOffset = 48 + $i * 48
        $entryKey = '{0:X8}:{1:X8}' -f [BitConverter]::ToUInt32($manifest, $entryOffset), [BitConverter]::ToUInt32($manifest, $entryOffset + 4)
        if ($removeKeys.Contains($entryKey)) { continue }

        $referenceOffset = [BitConverter]::ToUInt32($manifest, $entryOffset + 16)
        $referenceCount = [BitConverter]::ToUInt32($manifest, $entryOffset + 20)
        for ($referenceIndex = 0; $referenceIndex -lt $referenceCount; $referenceIndex++) {
            $offset = $referencesStart + $referenceOffset + $referenceIndex * 8
            $referenceKey = '{0:X8}:{1:X8}' -f [BitConverter]::ToUInt32($manifest, $offset), [BitConverter]::ToUInt32($manifest, $offset + 4)
            if ($removeKeys.Contains($referenceKey)) {
                throw "Retained asset $entryKey still references compilation-only asset $referenceKey."
            }
        }
    }

    $streamBytes = @{}
    foreach ($extension in @('bin', 'relo', 'imp')) {
        $streamBytes[$extension] = [IO.File]::ReadAllBytes((Join-Path $OutputRoot "mod.$extension"))
    }
    $streamOffsets = @{ bin = 4L; relo = 4L; imp = 4L }
    $streamOutputs = @{}
    foreach ($extension in @('bin', 'relo', 'imp')) {
        $output = New-Object System.IO.MemoryStream
        $output.Write($streamBytes[$extension], 0, 4)
        $streamOutputs[$extension] = $output
    }

    $keptEntries = New-Object System.IO.MemoryStream
    $keptReferences = New-Object System.IO.MemoryStream
    $keptCount = 0
    $removedCount = 0
    $totalInstanceData = 0L
    $maxBin = 0
    $maxRelo = 0
    $maxImp = 0
    for ($i = 0; $i -lt $count; $i++) {
        $entryOffset = 48 + $i * 48
        $typeId = [BitConverter]::ToUInt32($manifest, $entryOffset)
        $instanceId = [BitConverter]::ToUInt32($manifest, $entryOffset + 4)
        $sizes = @{
            bin = [BitConverter]::ToUInt32($manifest, $entryOffset + 32)
            relo = [BitConverter]::ToUInt32($manifest, $entryOffset + 36)
            imp = [BitConverter]::ToUInt32($manifest, $entryOffset + 40)
        }
        $assetKey = '{0:X8}:{1:X8}' -f $typeId, $instanceId
        $remove = $removeKeys.Contains($assetKey)
        if ($remove) {
            ++$removedCount
        }
        else {
            $entry = [byte[]]::new(48)
            [Array]::Copy($manifest, $entryOffset, $entry, 0, 48)
            $referenceOffset = [BitConverter]::ToUInt32($manifest, $entryOffset + 16)
            $referenceCount = [BitConverter]::ToUInt32($manifest, $entryOffset + 20)
            [BitConverter]::GetBytes([uint32]$keptReferences.Length).CopyTo($entry, 16)
            if ($referenceCount -gt 0) {
                $referenceLength = $referenceCount * 8
                $keptReferences.Write($manifest, $referencesStart + $referenceOffset, $referenceLength)
            }
            $keptEntries.Write($entry, 0, $entry.Length)
            ++$keptCount
            $totalInstanceData += $sizes.bin
            $maxBin = [Math]::Max($maxBin, $sizes.bin)
            $maxRelo = [Math]::Max($maxRelo, $sizes.relo)
            $maxImp = [Math]::Max($maxImp, $sizes.imp)
            foreach ($extension in @('bin', 'relo', 'imp')) {
                if ($sizes[$extension] -gt 0) {
                    $streamOutputs[$extension].Write($streamBytes[$extension], $streamOffsets[$extension], $sizes[$extension])
                }
            }
        }
        foreach ($extension in @('bin', 'relo', 'imp')) { $streamOffsets[$extension] += $sizes[$extension] }
    }
    if ($removedCount -ne $compilationOnlyAssetSpecs.Count) {
        throw "Expected to remove $($compilationOnlyAssetSpecs.Count) compilation-only assets, removed $removedCount."
    }

    $header = [byte[]]::new(48)
    [Array]::Copy($manifest, 0, $header, 0, 48)
    [BitConverter]::GetBytes([uint32]$keptCount).CopyTo($header, 12)
    [BitConverter]::GetBytes([uint32]$totalInstanceData).CopyTo($header, 16)
    [BitConverter]::GetBytes([uint32]$maxBin).CopyTo($header, 20)
    [BitConverter]::GetBytes([uint32]$maxRelo).CopyTo($header, 24)
    [BitConverter]::GetBytes([uint32]$maxImp).CopyTo($header, 28)
    [BitConverter]::GetBytes([uint32]$keptReferences.Length).CopyTo($header, 32)
    $trailingStart = $referencesStart + [BitConverter]::ToUInt32($manifest, 32)
    $manifestOut = New-Object System.IO.MemoryStream
    $manifestOut.Write($header, 0, 48)
    $entryBytes = $keptEntries.ToArray()
    $manifestOut.Write($entryBytes, 0, $entryBytes.Length)
    $referenceBytes = $keptReferences.ToArray()
    $manifestOut.Write($referenceBytes, 0, $referenceBytes.Length)
    $manifestOut.Write($manifest, $trailingStart, $manifest.Length - $trailingStart)
    [IO.File]::WriteAllBytes($manifestPath, $manifestOut.ToArray())
    foreach ($extension in @('bin', 'relo', 'imp')) {
        [IO.File]::WriteAllBytes((Join-Path $OutputRoot "mod.$extension"), $streamOutputs[$extension].ToArray())
    }
    Write-Host "  removed $removedCount compilation-only original semantic assets"
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

function Ensure-RenamedWorldbuilderArtAssets {
    param([string]$BuiltModsRoot)

    $manifestPath = Join-Path $BuiltModsRoot 'worldbuilder.manifest'
    $bytes = [IO.File]::ReadAllBytes($manifestPath)
    $count = [BitConverter]::ToUInt32($bytes, 12)
    $artTypes = New-Object 'System.Collections.Generic.HashSet[uint32]'
    foreach ($hex in @('F0F08712', 'C2B1A262', '61D7EA40', '21E727DA', '2448AE30', 'E3181C04')) {
        [void]$artTypes.Add([Convert]::ToUInt32($hex, 16))
    }
    $newIds = New-Object 'System.Collections.Generic.HashSet[uint32]'
    foreach ($rename in $renamedArtAssets) { [void]$newIds.Add($rename.NewId) }
    $existingAliases = 0
    for ($i = 0; $i -lt $count; $i++) {
        $o = 48 + $i * 48
        if ($artTypes.Contains([BitConverter]::ToUInt32($bytes, $o)) -and $newIds.Contains([BitConverter]::ToUInt32($bytes, $o + 4))) {
            ++$existingAliases
        }
    }
    if ($existingAliases -gt 0) {
        Write-Host "  WorldBuilder cache already contains $existingAliases Raya art aliases"
        return
    }

    $binOffsets = [long[]]::new($count)
    $reloOffsets = [long[]]::new($count)
    $impOffsets = [long[]]::new($count)
    $binCursor = 4L
    $reloCursor = 4L
    $impCursor = 4L
    for ($i = 0; $i -lt $count; $i++) {
        $o = 48 + $i * 48
        $binOffsets[$i] = $binCursor
        $reloOffsets[$i] = $reloCursor
        $impOffsets[$i] = $impCursor
        $binCursor += [BitConverter]::ToUInt32($bytes, $o + 32)
        $reloCursor += [BitConverter]::ToUInt32($bytes, $o + 36)
        $impCursor += [BitConverter]::ToUInt32($bytes, $o + 40)
    }

    $clones = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $count; $i++) {
        $o = 48 + $i * 48
        $typeId = [BitConverter]::ToUInt32($bytes, $o)
        if (-not $artTypes.Contains($typeId)) { continue }
        $instanceId = [BitConverter]::ToUInt32($bytes, $o + 4)
        foreach ($rename in $renamedArtAssets) {
            if ($instanceId -eq $rename.OldId) {
                $clones.Add([pscustomobject]@{ Index = $i; EntryOffset = $o; Rename = $rename })
            }
        }
    }
    if ($clones.Count -eq 0) { throw 'No WorldBuilder art entries matched the Raya rename table.' }

    $oldRefSize = [BitConverter]::ToUInt32($bytes, 32)
    $referencedManifestSize = [BitConverter]::ToUInt32($bytes, 36)
    $oldNameSize = [BitConverter]::ToUInt32($bytes, 40)
    $sourceSize = [BitConverter]::ToUInt32($bytes, 44)
    $oldEntriesEnd = 48 + $count * 48
    $oldRefsStart = $oldEntriesEnd
    $referencedManifestStart = $oldRefsStart + $oldRefSize
    $oldNamesStart = $referencedManifestStart + $referencedManifestSize
    $sourceStart = $oldNamesStart + $oldNameSize

    $newRefs = New-Object System.IO.MemoryStream
    $newNames = New-Object System.IO.MemoryStream
    $nameOffsets = @{}
    foreach ($rename in $renamedArtAssets) {
        $nameOffsets[$rename.NewId] = $oldNameSize + $newNames.Length
        $nameBytes = [Text.Encoding]::ASCII.GetBytes($rename.Name + [char]0)
        $newNames.Write($nameBytes, 0, $nameBytes.Length)
    }

    function Read-StreamChunk([string]$Path, [long]$Offset, [int]$Length) {
        if ($Length -eq 0) { return [byte[]]::new(0) }
        $result = [byte[]]::new($Length)
        $stream = [IO.File]::OpenRead($Path)
        try {
            [void]$stream.Seek($Offset, [IO.SeekOrigin]::Begin)
            $read = $stream.Read($result, 0, $Length)
            if ($read -ne $Length) { throw "Short read from $Path at $Offset ($read/$Length)." }
        }
        finally { $stream.Dispose() }
        return $result
    }

    $streamAppends = @{ bin = New-Object System.IO.MemoryStream; relo = New-Object System.IO.MemoryStream; imp = New-Object System.IO.MemoryStream }
    $cloneEntries = New-Object System.IO.MemoryStream
    foreach ($clone in $clones) {
        $entry = [byte[]]::new(48)
        [Array]::Copy($bytes, $clone.EntryOffset, $entry, 0, 48)
        [BitConverter]::GetBytes([uint32]$clone.Rename.NewId).CopyTo($entry, 4)
        [BitConverter]::GetBytes([uint32]($oldRefSize + $newRefs.Length)).CopyTo($entry, 16)
        [BitConverter]::GetBytes([uint32]$nameOffsets[$clone.Rename.NewId]).CopyTo($entry, 24)

        $refOffset = [BitConverter]::ToUInt32($bytes, $clone.EntryOffset + 16)
        $refCount = [BitConverter]::ToUInt32($bytes, $clone.EntryOffset + 20)
        for ($r = 0; $r -lt $refCount; $r++) {
            $reference = [byte[]]::new(8)
            [Array]::Copy($bytes, $oldRefsStart + $refOffset + $r * 8, $reference, 0, 8)
            $referenceId = [BitConverter]::ToUInt32($reference, 4)
            foreach ($rename in $renamedArtAssets) {
                if ($referenceId -eq $rename.OldId) { [BitConverter]::GetBytes([uint32]$rename.NewId).CopyTo($reference, 4) }
            }
            $newRefs.Write($reference, 0, 8)
        }
        $cloneEntries.Write($entry, 0, 48)

        $index = $clone.Index
        $binSize = [BitConverter]::ToUInt32($entry, 32)
        $reloSize = [BitConverter]::ToUInt32($entry, 36)
        $impSize = [BitConverter]::ToUInt32($entry, 40)
        if ($binSize -gt 0) {
            $chunk = Read-StreamChunk (Join-Path $BuiltModsRoot 'worldbuilder.bin') $binOffsets[$index] $binSize
            $streamAppends.bin.Write($chunk, 0, $chunk.Length)
        }
        if ($reloSize -gt 0) {
            $chunk = Read-StreamChunk (Join-Path $BuiltModsRoot 'worldbuilder.relo') $reloOffsets[$index] $reloSize
            $streamAppends.relo.Write($chunk, 0, $chunk.Length)
        }
        if ($impSize -gt 0) {
            $chunk = Read-StreamChunk (Join-Path $BuiltModsRoot 'worldbuilder.imp') $impOffsets[$index] $impSize
            $streamAppends.imp.Write($chunk, 0, $chunk.Length)
        }
    }

    $header = [byte[]]::new(48)
    [Array]::Copy($bytes, 0, $header, 0, 48)
    [BitConverter]::GetBytes([uint32]($count + $clones.Count)).CopyTo($header, 12)
    [BitConverter]::GetBytes([uint32]([BitConverter]::ToUInt32($bytes, 16) + $streamAppends.bin.Length)).CopyTo($header, 16)
    [BitConverter]::GetBytes([uint32]($oldRefSize + $newRefs.Length)).CopyTo($header, 32)
    [BitConverter]::GetBytes([uint32]($oldNameSize + $newNames.Length)).CopyTo($header, 40)

    $manifestOut = New-Object System.IO.MemoryStream
    $manifestOut.Write($header, 0, 48)
    $manifestOut.Write($bytes, 48, $count * 48)
    $cloneEntryBytes = $cloneEntries.ToArray()
    $manifestOut.Write($cloneEntryBytes, 0, $cloneEntryBytes.Length)
    $manifestOut.Write($bytes, $oldRefsStart, $oldRefSize)
    $newRefBytes = $newRefs.ToArray()
    $manifestOut.Write($newRefBytes, 0, $newRefBytes.Length)
    $manifestOut.Write($bytes, $referencedManifestStart, $referencedManifestSize)
    $manifestOut.Write($bytes, $oldNamesStart, $oldNameSize)
    $newNameBytes = $newNames.ToArray()
    $manifestOut.Write($newNameBytes, 0, $newNameBytes.Length)
    $manifestOut.Write($bytes, $sourceStart, $sourceSize)

    foreach ($extension in @('bin', 'relo', 'imp')) {
        $path = Join-Path $BuiltModsRoot "worldbuilder.$extension"
        $append = [IO.File]::Open($path, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $chunkBytes = $streamAppends[$extension].ToArray()
            $append.Write($chunkBytes, 0, $chunkBytes.Length)
        }
        finally { $append.Dispose() }
    }
    [IO.File]::WriteAllBytes($manifestPath, $manifestOut.ToArray())
    Write-Host "  WorldBuilder cache extended with $($clones.Count) Raya art aliases"
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

    Ensure-RenamedWorldbuilderArtAssets -BuiltModsRoot $builtMods

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

function Rename-ResolvedArtAssetIds {
    param([string]$OutputRoot)

    $manifestPath = Join-Path $OutputRoot 'mod.manifest'
    $bytes = [IO.File]::ReadAllBytes($manifestPath)
    $count = [BitConverter]::ToUInt32($bytes, 12)
    $artTypes = New-Object 'System.Collections.Generic.HashSet[uint32]'
    foreach ($hex in @('F0F08712', 'C2B1A262', '61D7EA40', '21E727DA', '2448AE30', 'E3181C04')) {
        [void]$artTypes.Add([Convert]::ToUInt32($hex, 16))
    }

    $existingKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    $newNameOffsets = @{}
    for ($i = 0; $i -lt $count; $i++) {
        $offset = 48 + $i * 48
        $typeId = [BitConverter]::ToUInt32($bytes, $offset)
        $instanceId = [BitConverter]::ToUInt32($bytes, $offset + 4)
        [void]$existingKeys.Add(('{0:X8}:{1:X8}' -f $typeId, $instanceId))
        foreach ($rename in $renamedArtAssets) {
            if ($instanceId -eq $rename.NewId -and -not $newNameOffsets.ContainsKey($rename.NewId)) {
                $newNameOffsets[$rename.NewId] = [BitConverter]::ToUInt32($bytes, $offset + 24)
            }
        }
    }

    $renamedEntries = 0
    for ($i = 0; $i -lt $count; $i++) {
        $offset = 48 + $i * 48
        $typeId = [BitConverter]::ToUInt32($bytes, $offset)
        if (-not $artTypes.Contains($typeId)) { continue }
        $instanceId = [BitConverter]::ToUInt32($bytes, $offset + 4)
        foreach ($rename in $renamedArtAssets) {
            if ($instanceId -ne $rename.OldId) { continue }
            $newKey = '{0:X8}:{1:X8}' -f $typeId, $rename.NewId
            if ($existingKeys.Contains($newKey)) {
                throw "Resolved art manifest contains both old and new assets for $newKey."
            }
            if (-not $newNameOffsets.ContainsKey($rename.NewId)) {
                throw "Resolved art manifest has no name offset for $($rename.Name)."
            }
            [BitConverter]::GetBytes([uint32]$rename.NewId).CopyTo($bytes, $offset + 4)
            [BitConverter]::GetBytes([uint32]$newNameOffsets[$rename.NewId]).CopyTo($bytes, $offset + 24)
            ++$renamedEntries
            break
        }
    }

    $referenceBytes = [BitConverter]::ToUInt32($bytes, 32)
    $referencesStart = 48 + $count * 48
    for ($offset = $referencesStart; $offset -lt $referencesStart + $referenceBytes; $offset += 8) {
        $typeId = [BitConverter]::ToUInt32($bytes, $offset)
        if (-not $artTypes.Contains($typeId)) { continue }
        $instanceId = [BitConverter]::ToUInt32($bytes, $offset + 4)
        foreach ($rename in $renamedArtAssets) {
            if ($instanceId -eq $rename.OldId) {
                [BitConverter]::GetBytes([uint32]$rename.NewId).CopyTo($bytes, $offset + 4)
                break
            }
        }
    }

    if ($renamedEntries -eq 0) { throw 'Resolver produced no residual same-name art assets to rename.' }
    [IO.File]::WriteAllBytes($manifestPath, $bytes)
    Write-Host "  renamed $renamedEntries residual same-name art assets"
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
Add-CampaignCloneSources -StagedSource $staged6
Add-WeaponCloneSources -StagedSource $staged6

$raw6 = Join-Path $WorkRoot 'raw-game6'
New-Item -ItemType Directory -Force -Path $raw6 | Out-Null
Write-FilteredWorldbuilderManifest -OutputRoot $raw6
Invoke-AssetBuilder -StagedSource $staged6 -OutputRoot $raw6
Normalize-ManifestProvenance -ManifestPath (Join-Path $raw6 'mod.manifest') -StagedSource $staged6
Redirect-ClonedWeaponReferences -ManifestPath (Join-Path $raw6 'mod.manifest')
Invoke-HashFix -ManifestPath (Join-Path $raw6 'mod.manifest')
Remove-CompilationOnlyAssets -OutputRoot $raw6
Invoke-HashFix -ManifestPath (Join-Path $raw6 'mod.manifest')
$rawCount6 = Assert-AssetGates -Bytes ([IO.File]::ReadAllBytes((Join-Path $raw6 'mod.manifest'))) -ExpectedKeys $expectedGameObjectKeys -Label 'game6-raw'
Assert-OwnedSemanticAssetIds -ManifestPath (Join-Path $raw6 'mod.manifest') -StringHashesPath (Join-Path $raw6 'StringHashes.xml') -Label 'game6-raw'
Write-Host "  raw asset count: $rawCount6"

$merged6 = Invoke-ModAssetResolver -RawRoot $raw6
Rename-ResolvedArtAssetIds -OutputRoot $merged6
Remove-BuildOnlyManifestReference -ManifestPath (Join-Path $merged6 'mod.manifest') -ReferencePath 'worldbuilder.manifest'
Invoke-HashFix -ManifestPath (Join-Path $merged6 'mod.manifest')
$mergedBytes = [IO.File]::ReadAllBytes((Join-Path $merged6 'mod.manifest'))
$finalCount6 = Assert-AssetGates -Bytes $mergedBytes -ExpectedKeys $expectedGameObjectKeys -Label 'game6-final'
Assert-OwnedSemanticAssetIds -ManifestPath (Join-Path $merged6 'mod.manifest') -StringHashesPath (Join-Path $raw6 'StringHashes.xml') -Label 'game6-final'
Assert-RenamedArtAssets -ManifestBytes $mergedBytes -Label 'game6-final'
Assert-RuntimeManifestReferences -ManifestBytes $mergedBytes -Label 'game6-final'
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
