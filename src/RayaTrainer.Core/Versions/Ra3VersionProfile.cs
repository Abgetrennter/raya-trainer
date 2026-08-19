using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

public sealed class Ra3VersionProfile
{
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

    private IReadOnlyDictionary<string, VersionedAddress> Catalog(string catalogName)
    {
        return catalogName switch
        {
            "Hooks" => Hooks,
            "RemoteGlobals" => RemoteGlobals,
            "EngineFunctions" => EngineFunctions,
            _ => throw new ArgumentOutOfRangeException(nameof(catalogName), catalogName, "Unknown version profile catalog.")
        };
    }
}
