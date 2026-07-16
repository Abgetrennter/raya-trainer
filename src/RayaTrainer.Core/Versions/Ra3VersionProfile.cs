using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

public sealed class Ra3VersionProfile
{
    private const uint MaximumModuleSpan = 0x02000000;

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string ProcessName { get; init; }

    public required IReadOnlySet<string> FileVersions { get; init; }

    public int ModuleBaseVa { get; init; } = 0x400000;

    public bool IsPatchInstallable { get; init; } = true;

    public bool SupportsAgentBackend { get; init; } = true;

    public bool SupportsDirectGameApi { get; init; } = true;

    public bool SupportsSignatureScanning { get; init; }

    public required IReadOnlyDictionary<string, VersionedAddress> Hooks { get; init; }

    public required IReadOnlyDictionary<string, VersionedAddress> RemoteGlobals { get; init; }

    public required IReadOnlyDictionary<string, VersionedAddress> EngineFunctions { get; init; }

    public required IReadOnlyDictionary<string, VersionedAddress> NativeAgentRefs { get; init; }

    /// <summary>
    /// Manifest hooks intentionally replaced by another native hook for this profile.
    /// They are neither installed nor reported as missing.
    /// </summary>
    public IReadOnlySet<string> SupersededHooks { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Version-independent scanner entries that this profile intentionally does not use.
    /// Their scan result may be zero without weakening validation of active symbols.
    /// </summary>
    public IReadOnlySet<string> OptionalSignatureSymbols { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool MatchesFileVersion(string fileVersion)
    {
        return FileVersions.Contains(fileVersion.Trim());
    }

    public bool MatchesFileVersionFamily(string fileVersion)
    {
        if (!Version.TryParse(fileVersion.Trim(), out var candidate))
        {
            return false;
        }

        return FileVersions.Any(known =>
            Version.TryParse(known, out var expected) &&
            expected.Major == candidate.Major &&
            expected.Minor == candidate.Minor);
    }

    public bool MatchesProcessName(string processName)
    {
        return TrainerProcessName.Matches(ProcessName, processName);
    }

    public int ResolveVerifiedRva(string catalogName, string symbolicName)
    {
        var catalog = Catalog(catalogName);
        if (!catalog.TryGetValue(symbolicName, out var address))
        {
            throw new UnsupportedSymbolException(Id, catalogName, symbolicName, AddressSupportStatus.Unsupported);
        }

        if (address.Status != AddressSupportStatus.Verified || address.Rva is null)
        {
            throw new UnsupportedSymbolException(Id, catalogName, symbolicName, address.Status);
        }

        return address.Rva.Value;
    }

    /// <summary>
    /// Builds the ordered native agent catalog RVAs (in <see cref="NativeAgentCatalog.EntryNames"/>
    /// order) for delivery to the injected DLL via <c>SetNativeCatalog</c>.
    /// </summary>
    /// <exception cref="UnsupportedSymbolException">Any native agent ref is not verified.</exception>
    public IReadOnlyList<uint> BuildNativeAgentCatalogRvas()
    {
        var rvas = new uint[NativeAgentCatalog.ExpectedEntryCount];
        for (var index = 0; index < NativeAgentCatalog.ExpectedEntryCount; index++)
        {
            var name = NativeAgentCatalog.EntryNames[index];
            rvas[index] = (uint)ResolveVerifiedRva("NativeAgentRefs", name);
        }

        return rvas;
    }

    /// <summary>
    /// Builds the ordered native agent catalog RVAs, preferring signature-scanned
    /// addresses for address-class entries (global pointers and engine functions)
    /// and falling back to the profile's verified RVA when a scan is unavailable or
    /// missed. Scanner results are converted from VA using the live module base when
    /// supplied. Constant-class entries (structure offsets and mode flags) always use
    /// the profile value.
    /// </summary>
    public IReadOnlyList<uint> BuildNativeAgentCatalogRvas(
        IReadOnlyDictionary<string, uint>? scannedAddresses,
        bool requireScannedAddresses = false,
        uint? actualModuleBaseVa = null)
    {
        if (scannedAddresses is null)
        {
            if (requireScannedAddresses)
            {
                throw new InvalidOperationException("签名兼容候选缺少 Native 地址扫描结果，禁止使用固定 RVA。");
            }

            return BuildNativeAgentCatalogRvas();
        }

        var rvas = new uint[NativeAgentCatalog.ExpectedEntryCount];
        var scannedModuleBase = actualModuleBaseVa ?? checked((uint)ModuleBaseVa);
        for (var index = 0; index < NativeAgentCatalog.ExpectedEntryCount; index++)
        {
            var name = NativeAgentCatalog.EntryNames[index];
            var fixedRva = (uint)ResolveVerifiedRva("NativeAgentRefs", name);

            if (NativeAgentRefSignatureMapping.TryGetSignatureKey(name, out var sigKey)
                && scannedAddresses.TryGetValue(sigKey, out var scanned) && scanned != 0)
            {
                // The signature scanner returns absolute virtual addresses (VA), but the
                // native agent catalog expects RVAs (the DLL adds the module base internally).
                if (scanned < scannedModuleBase || scanned - scannedModuleBase >= MaximumModuleSpan)
                {
                    throw new InvalidOperationException(
                        $"签名地址 0x{scanned:X8} 超出目标模块的允许范围：{sigKey}（{name}）。");
                }

                rvas[index] = scanned - scannedModuleBase;
            }
            else if (requireScannedAddresses &&
                     NativeAgentRefSignatureMapping.TryGetSignatureKey(name, out sigKey) &&
                     !OptionalSignatureSymbols.Contains(sigKey))
            {
                throw new InvalidOperationException($"签名兼容候选的必需 Native 地址未唯一定位：{sigKey}（{name}）。");
            }
            else
            {
                rvas[index] = fixedRva;
            }
        }

        return rvas;
    }

    private IReadOnlyDictionary<string, VersionedAddress> Catalog(string catalogName)
    {
        return catalogName switch
        {
            "Hooks" => Hooks,
            "RemoteGlobals" => RemoteGlobals,
            "EngineFunctions" => EngineFunctions,
            "NativeAgentRefs" => NativeAgentRefs,
            _ => throw new ArgumentOutOfRangeException(nameof(catalogName), catalogName, "Unknown version profile catalog.")
        };
    }
}
