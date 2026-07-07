using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Tests;

internal static class TestAgentSignatureCatalog
{
    public static AgentSignatureScanPayload CreateForProfile(
        Ra3VersionProfile profile,
        uint moduleBase = 0x400000)
    {
        var addresses = profile.Hooks.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status == AddressSupportStatus.Verified && entry.Value.Rva is not null
                    ? moduleBase + checked((uint)entry.Value.Rva.Value)
                    : 0u,
                StringComparer.OrdinalIgnoreCase);

        return new AgentSignatureScanPayload(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            checked((uint)addresses.Count),
            checked((uint)addresses.Count(entry => entry.Value != 0)),
            addresses);
    }

    public static AgentSignatureScanPayload CreateRa3112(
        uint moduleBase = 0x400000,
        IReadOnlyDictionary<string, uint>? overrides = null)
    {
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var addresses = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profile.Hooks)
        {
            addresses[entry.Key] = moduleBase + checked((uint)entry.Value.Rva!.Value);
        }
        if (overrides is not null)
        {
            foreach (var entry in overrides)
            {
                addresses[entry.Key] = entry.Value;
            }
        }

        return new AgentSignatureScanPayload(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            checked((uint)addresses.Count),
            checked((uint)addresses.Count),
            addresses);
    }
}
