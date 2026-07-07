using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// The native agent reference catalog delivered from the host to the injected DLL via the
/// <see cref="AgentCommand.SetNativeCatalog"/> command. Entries contain both game-module
/// RVAs and profile-specific structure offsets; consumers interpret each value by name.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is an ordered list of game-module RVAs. The order is fixed by
/// <see cref="EntryNames"/> and is shared verbatim between the C# host and the C++ DLL; the
/// DLL stores entries by index, so a mismatch in ordering or count is a protocol violation
/// (the DLL rejects the catalog). This replaces the DLL's compile-time 1.12 RVAs
/// (<c>kGameClientPointerRva</c>, <c>kGameApiCatalog</c>) with a per-profile runtime table.
/// </para>
/// <para>
/// <b>Wire format</b>: <c>uint32 EntryCount</c> followed by <c>EntryCount</c> little-endian
/// <c>uint32</c> RVAs in <see cref="EntryNames"/> order. The response is a standard
/// <see cref="AgentCommandResultPayload"/>.
/// </para>
/// </remarks>
public static class NativeAgentCatalog
{
    /// <summary>
    /// The canonical, ordered native agent reference names. The C++ mirror
    /// (<c>kNativeCatalogEntries</c>) must stay byte-for-byte in sync with this order.
    /// </summary>
    public static readonly IReadOnlyList<string> EntryNames =
    [
        "GameClientPointer",
        "GetThingClass",
        "LevelUpSelected",
        "CreateUnit",
        "KillUnit",
        "PlayerManager",
        "GetCurrentPlayer",
        "ThingTemplateStore",
        "SelectedUnitCode",
        "ScienceStore",
        "ScienceStoreFindScience",
        "ScienceStoreFindUpgrade",
        "ScienceManagerFindScience",
        "PlayerGetUpgradeStore",
        "ScienceManagerHasScience",
        "PlayerGrantScience",
        "PlayerScienceManagerOffset",
        "SelectionManager",
        "SelectionListHeadOffset",
        "SelectionCountOffset",
        "MouseWorldPointer",
        "MouseWorldToMapPosition",
        "ObjectSetPosition",
        "MovementModuleOffset",
        "MovementContainerOffset",
        "ObjectOwnerOffset",
        "BodyOffset",
        "VeterancyOffset",
        "WeaponContainerOffset",
        "UnitStatePrimaryOffset",
        "UnitStateSecondaryOffset",
        "OneHitDamageDeltaMode",
        "OneHitCaller1",
        "OneHitCaller2",
        "OneHitCaller3",
        "DestroySelectionListHeadOffset",
        "ProductionModulesOffset",
        "LocalContextSiblingOffset",
        "RestoreOreCapacityMode"
    ];

    /// <summary>
    /// Expected entry count for a complete catalog. The DLL requires exactly this many
    /// entries before it will service DirectGameApi requests that depend on native RVAs.
    /// </summary>
    public const int ExpectedEntryCount = 39;

    /// <summary>
    /// Encodes a catalog payload from an ordered RVA list. <paramref name="rvas"/> must be
    /// in <see cref="EntryNames"/> order and exactly <see cref="ExpectedEntryCount"/> long.
    /// </summary>
    public static byte[] Encode(IReadOnlyList<uint> rvas)
    {
        if (rvas.Count != ExpectedEntryCount)
        {
            throw new ArgumentException(
                $"Native agent catalog must contain exactly {ExpectedEntryCount} entries, got {rvas.Count}.",
                nameof(rvas));
        }

        var buffer = new byte[sizeof(uint) * (1 + ExpectedEntryCount)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, sizeof(uint)), (uint)ExpectedEntryCount);
        for (var index = 0; index < ExpectedEntryCount; index++)
        {
            var offset = sizeof(uint) * (1 + index);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)), rvas[index]);
        }

        return buffer;
    }
}
