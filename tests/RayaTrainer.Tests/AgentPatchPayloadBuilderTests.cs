using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentPatchPayloadBuilderTests
{
    [Fact]
    public void Ra3_1_12_without_scan_uses_fixed_rva()
    {
        // Use the real ra3_1.12 profile via TrainerTarget resolution
        var target = new TrainerTarget(
            GameTarget.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            VersionProfileId: "ra3_1.12");
        var manifest = BuildManifest();
        var status = new AgentStatusPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0, 0, 0);

        var result = AgentPatchPayloadBuilder.BuildWithDiagnostics(manifest, target, status, scannedAddresses: null);

        // hook address = module base + 0x6CFDFE = 0xACFDFE
        Assert.Contains(result.Request.Hooks, h => h.Address == 0xACFDFEu);
    }

    [Fact]
    public void Ra3_1_12_with_scan_hit_uses_scanned_address()
    {
        var target = new TrainerTarget(
            GameTarget.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            VersionProfileId: "ra3_1.12");
        var manifest = BuildManifest();
        var status = new AgentStatusPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0, 0, 0);
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["_BackPlayerMoney"] = 0xA64E9E
        };

        var result = AgentPatchPayloadBuilder.BuildWithDiagnostics(manifest, target, status, scanned);

        // scanned address 0xA64E9E wins over fixed RVA
        Assert.Contains(result.Request.Hooks, h => h.Address == 0xA64E9Eu);
    }

    [Fact]
    public void Ra3_1_12_with_scan_miss_falls_back_to_fixed_rva()
    {
        var target = new TrainerTarget(
            GameTarget.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            VersionProfileId: "ra3_1.12");
        var manifest = BuildManifest();
        var status = new AgentStatusPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0, 0, 0);
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            // _BackPlayerMoney scanned to 0 → miss → fallback to fixed RVA
            ["_BackPlayerMoney"] = 0
        };

        var result = AgentPatchPayloadBuilder.BuildWithDiagnostics(manifest, target, status, scanned);

        // fallback to fixed RVA = 0xACFDFE
        Assert.Contains(result.Request.Hooks, h => h.Address == 0xACFDFEu);
    }

    [Fact]
    public void Signature_compatibility_candidate_never_falls_back_to_fixed_rva()
    {
        var target = new TrainerTarget(
            GameTarget.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            VersionProfileId: "ra3_1.12",
            SignatureCompatibilityMode: true);
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["_BackPlayerMoney"] = 0
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AgentPatchPayloadBuilder.BuildWithDiagnostics(
                BuildManifest(),
                target,
                new AgentStatusPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0, 0, 0),
                scanned));

        Assert.Contains("未唯一定位", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Signature_compatibility_candidate_uses_attested_live_bytes()
    {
        var target = new TrainerTarget(
            GameTarget.ProcessName,
            0x400000,
            Is32Bit: true,
            VersionSupported: true,
            VersionProfileId: "ra3_1.12",
            SignatureCompatibilityMode: true);
        var liveBytes = new byte[] { 0x03, 0x78, 0x04, 0x8B, 0x11 };

        var result = AgentPatchPayloadBuilder.BuildWithDiagnostics(
            BuildManifest(),
            target,
            new AgentStatusPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0, 0, 0),
            new Dictionary<string, uint> { ["_BackPlayerMoney"] = 0xA65E9E },
            new Dictionary<string, byte[]> { ["_BackPlayerMoney"] = liveBytes });

        Assert.Equal(liveBytes, Assert.Single(result.Request.Hooks).OriginalBytes);
    }

    private static TrainerManifest BuildManifest()
    {
        // A single hook matching _BackPlayerMoney from the real embedded manifest at RVA 0x6CFDFE
        var hook = new PatchHook(
            Address: "ra3_1.12.game+6CFDFE",
            SectionTitle: "Player Money Code",
            PatchAssembly: [],
            TrampolineTarget: null,
            ReturnLabel: "_BackPlayerMoney",
            EnableFlags: ["Moeny"],
            OriginalAssembly: ["add edi,[eax+04]", "mov edx,[ecx]"]);
        return new TrainerManifest(
            GameTarget.ProcessName,
            [],
            new PatchManifest([hook]),
            []);
    }
}
