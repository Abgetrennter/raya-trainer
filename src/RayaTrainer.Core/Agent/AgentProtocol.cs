namespace RayaTrainer.Core.Agent;

public static class AgentProtocol
{
    public const uint Magic = 0x41594152;

    /// <summary>
    /// Protocol version. Bumped 1 -> 2 to add the <see cref="AgentCommand.SetNativeCatalog"/>
    /// command, which delivers per-profile native/game API RVAs to the injected DLL so it no
    /// longer depends on compile-time 1.12 RVAs. Host and DLL must agree on the exact version.
    /// Bumped 2 -> 3 to add the <see cref="AgentCommand.GetMismatchDiagnostics"/> command,
    /// which lets the DLL report the actual bytes it observed at a failing hook site so the
    /// host can emit a PatchMismatchReport instead of only seeing a bare PatchMismatch status.
    /// Bumped 3 -> 4 to add <see cref="AgentCommand.ScanSignatures"/> and
    /// <see cref="AgentCommand.GetGameMode"/>, keeping signature resolution and semantic
    /// game-state reads inside the DLL.
    /// Bumped 4 -> 5 to add <see cref="AgentCommand.ExpandProductionQueue"/>.
    /// Bumped 5 -> 6 to add the Agent build fingerprint to Ping/GetStatus so a restarted
    /// trainer can safely reconnect to an already injected matching DLL.
    /// Bumped 6 -> 7 to add <see cref="AgentCommand.TeleportSelectedUnitsToMouse"/>.
    /// Bumped 7 -> 8 for Native Hook IDs, DLL-internal feature state, and removal of the
    /// native runtime contract.
    /// Bumped 8 -> 9 for the Ra3Trainer -> RayaTrainer product rename. Magic changed from
    /// "RA3T" (0x54334152) to "RAYA" (0x41594152); fingerprint rotated to match. Legacy
    /// Agent pipe (Ra3Trainer.Agent.&lt;pid&gt;) is detected and refused at injection time.
    /// Bumped 9 -> 10 for object-level unit upgrade grant: commands
    /// <see cref="AgentCommand.GetSelectedUnitUpgrades"/>/
    /// <see cref="AgentCommand.GrantObjectUpgradeOnSelectedSameType"/>, Native catalog entry
    /// <c>UpgradeTemplateTypeOffset</c> (EntryCount 40 -> 41), and the dispatcher Values slot
    /// widening (8 -> 24). An already-injected v9 Agent must not be reused: the catalog count
    /// and command set are incompatible.
    /// Bumped 10 -> 11 for the L1 wire protocol refactor. Commands 5/6/7 redefined:
    /// <see cref="AgentCommand.SetToggle"/>→<see cref="AgentCommand.SetFeatureStates"/>,
    /// <see cref="AgentCommand.TriggerAction"/>→<see cref="AgentCommand.SetRuntimePatchSet"/>,
    /// <see cref="AgentCommand.WriteResourceValues"/>→<see cref="AgentCommand.GetFeatureStates"/>.
    /// New capability bits <see cref="NativeRuntimeCapabilities.FeatureStateSnapshot"/> (0x8) and
    /// <see cref="NativeRuntimeCapabilities.RuntimePatchSets"/> (0x10); required mask becomes 0x1F.
    /// Removed contract entries for SetToggle, TriggerAction, WriteResourceValues, and
    /// AgentMemoryWriteRequest. An already-injected v10 Agent must not be reused.
    /// </summary>
    public const ushort Version = 11;
    public const int HeaderSize = 16;
    public const uint MaxPayloadLength = 64 * 1024;

    public static void Validate(AgentProtocolHeader header)
    {
        if (header.Magic != Magic)
        {
            throw new InvalidDataException($"Agent protocol magic mismatch. Expected 0x{Magic:X8}, actual 0x{header.Magic:X8}.");
        }

        if (header.Version != Version)
        {
            throw new InvalidDataException($"Agent protocol version mismatch. Expected {Version}, actual {header.Version}.");
        }

        if (header.PayloadLength > MaxPayloadLength)
        {
            throw new InvalidDataException($"Agent payload length {header.PayloadLength} exceeds limit {MaxPayloadLength}.");
        }
    }
}
