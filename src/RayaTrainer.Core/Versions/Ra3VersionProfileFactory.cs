using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

internal static partial class Ra3VersionProfileFactory
{
    // The native agent ref order is owned by the host<->DLL protocol contract
    // (NativeAgentCatalog.EntryNames). Mirrored here so BuildNativeAgentRefs stays aligned
    // with the wire format without duplicating the literal names.
    public static readonly string[] NativeAgentRefNames = NativeAgentCatalog.EntryNames.ToArray();


    public static IReadOnlyDictionary<string, VersionedAddress> BuildHooks(
        IReadOnlyDictionary<int, int> verifiedRvas,
        string source,
        IReadOnlyDictionary<int, byte[]>? expectedBytesByLegacyRva = null,
        string? profileId = null)
    {
        return TrainerRuntimeAssets.LoadManifest().PatchManifest.Hooks
            .Where(hook => profileId is null || hook.SupportsProfile(profileId))
            .ToDictionary(
                HookKey,
                hook =>
                {
                    var key = HookKey(hook);
                    var rva = ParseRva(hook.Address);
                    return verifiedRvas.TryGetValue(rva, out var versionRva)
                        ? Verified(key, versionRva, source, ExpectedBytes(expectedBytesByLegacyRva, rva))
                        : NeedsReanalysis(key, $"No verified mapping for 1.12 RVA 0x{rva:X}.", source);
                },
                StringComparer.OrdinalIgnoreCase);
    }


    public static IReadOnlyDictionary<string, VersionedAddress> BuildUnsupportedNativeAgentRefs(
        IEnumerable<string> symbolicNames,
        string source)
    {
        return symbolicNames.ToDictionary(
            name => name,
            name => NeedsReanalysis(name, "Native Agent RVA has not been versioned for this profile.", source),
            StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, VersionedAddress> BuildNativeAgentRefs(
        IReadOnlyDictionary<string, int> verifiedRvas,
        string source)
    {
        return NativeAgentRefNames.ToDictionary(
            name => name,
            name => verifiedRvas.TryGetValue(name, out var rva)
                ? Verified(name, rva, source)
                : NeedsReanalysis(name, "Native Agent RVA has not been versioned for this profile.", source),
            StringComparer.OrdinalIgnoreCase);
    }

    public static VersionedAddress Verified(
        string symbolicName,
        int rva,
        string source,
        IReadOnlyList<byte>? expectedBytes = null)
    {
        return new VersionedAddress(symbolicName, rva, AddressSupportStatus.Verified, source, ExpectedBytes: expectedBytes);
    }

    public static VersionedAddress NeedsReanalysis(string symbolicName, string notes, string source)
    {
        return new VersionedAddress(symbolicName, null, AddressSupportStatus.NeedsReanalysis, source, notes);
    }

    public static IReadOnlyDictionary<string, VersionedAddress> BuildRemoteGlobals(
        IReadOnlyDictionary<string, int> verifiedRvas,
        string source)
    {
        var names = new[]
        {
            "GameClientPointer",
            "MouseWorldPointer",
            "SelectionManager",
            "ScienceStore",
            "ThingTemplateStore",
            "PlayerManager",
            "SelectedUnitCode"
        };

        return names.ToDictionary(
            name => name,
            name => verifiedRvas.TryGetValue(name, out var rva)
                ? Verified(name, rva, source)
                : NeedsReanalysis(name, "Remote global has not been verified for this version.", source),
            StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, VersionedAddress> BuildEngineFunctions(
        IReadOnlyDictionary<string, int> verifiedRvas,
        string source)
    {
        var names = new[]
        {
            "ScienceStore_FindScience",
            "ScienceStore_FindUpgrade",
            "MouseWorld_ToMapPosition",
            "CreateUnit",
            "LevelUpSelected",
            "KillUnit",
            "Object_SetPosition",
            "ThingTemplateStore_Find",
            "Player_GetScienceManager",
            "ScienceManager_FindScience",
            "Player_GetUpgradeStore",
            "ScienceManager_HasScience",
            "Player_GrantScience",
            "HashLookup"
        };

        return names.ToDictionary(
            name => name,
            name => verifiedRvas.TryGetValue(name, out var rva)
                ? Verified(name, rva, source)
                : NeedsReanalysis(name, "Engine function has not been verified for this version.", source),
            StringComparer.OrdinalIgnoreCase);
    }

    public static int ParseRva(string expression)
    {
        var plusIndex = expression.LastIndexOf('+');
        if (plusIndex < 0 || plusIndex == expression.Length - 1)
        {
            throw new InvalidDataException($"Address '{expression}' is not module-relative.");
        }

        return Convert.ToInt32(expression[(plusIndex + 1)..], 16);
    }

    private static IReadOnlyList<byte>? ExpectedBytes(
        IReadOnlyDictionary<int, byte[]>? expectedBytesByLegacyRva,
        int legacyRva)
    {
        return expectedBytesByLegacyRva is not null
            && expectedBytesByLegacyRva.TryGetValue(legacyRva, out var expectedBytes)
            ? expectedBytes
            : null;
    }

    private static string HookKey(PatchHook hook)
    {
        return string.IsNullOrWhiteSpace(hook.ReturnLabel)
            ? hook.Address
            : hook.ReturnLabel;
    }

}
