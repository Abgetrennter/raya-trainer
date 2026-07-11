namespace RayaTrainer.Core.Agent;

/// <summary>
/// Maps NativeAgentCatalog entry names that resolve to game-module addresses
/// (global pointers and engine functions) to their signature catalog keys.
/// Constant-class entries (structure offsets and mode flags) have no signature
/// and are intentionally absent from this table.
/// </summary>
public static class NativeAgentRefSignatureMapping
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameClientPointer"] = "Rva_8D8CE4",
            ["GetThingClass"] = "Rva_3E4230",
            ["LevelUpSelected"] = "Rva_35C200",
            ["CreateUnit"] = "Rva_205240",
            ["KillUnit"] = "Rva_39EA50",
            ["PlayerManager"] = "Rva_8E8C9C",
            ["GetCurrentPlayer"] = "Rva_4393E0",
            ["ThingTemplateStore"] = "Rva_8E6C58",
            ["SelectedUnitCode"] = "Rva_8E9838",
            ["ScienceStore"] = "Rva_8DBBF0",
            ["ScienceStoreFindScience"] = "Rva_1456F0",
            ["ScienceStoreFindUpgrade"] = "Rva_147260",
            ["ScienceManagerFindScience"] = "Rva_43C300",
            ["PlayerGetUpgradeStore"] = "Rva_44A2D0",
            ["ScienceManagerHasScience"] = "Rva_44D7C0",
            ["PlayerGrantScience"] = "Rva_454300",
            ["SelectionManager"] = "Rva_8DB73C",
            ["MouseWorldPointer"] = "Rva_8DAEFC",
            ["MouseWorldToMapPosition"] = "Rva_1ED4A0",
            ["ObjectSetPosition"] = "Rva_3E3D00",
            ["OneHitCaller1"] = "Rva_3AD79E",
            ["OneHitCaller2"] = "Rva_3ADEE2",
            ["OneHitCaller3"] = "Rva_38E651"
        };

    /// <summary>
    /// Returns the signature catalog key for the given native agent ref name, if any.
    /// Constant-class refs (offsets, mode flags) return false.
    /// </summary>
    public static bool TryGetSignatureKey(string refName, out string signatureKey)
    {
        return Map.TryGetValue(refName, out signatureKey!);
    }
}
