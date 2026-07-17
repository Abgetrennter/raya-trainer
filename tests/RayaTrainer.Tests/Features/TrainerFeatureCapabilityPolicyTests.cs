using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Features;

[Trait("Category", "Characterization")]
public sealed class TrainerFeatureCapabilityPolicyTests
{
    private readonly TrainerFeatureCapabilityPolicy _policy = new();

    // ── Test context implementation ──────────────────────────────────────────

    private sealed class TestCapabilityContext : ITrainerFeatureCapabilityContext
    {
        public bool IsAgentConnected { get; init; }
        public Ra3VersionProfile? CurrentProfile { get; init; }
        public IReadOnlyCollection<uint> InstalledNativeHookIds { get; init; } = Array.Empty<uint>();
        public IReadOnlyCollection<uint> RegisteredPatchSetIds { get; init; } = Array.Empty<uint>();
        public bool IsNativeCatalogDelivered { get; init; }
        public FeatureCapabilitySnapshot BaseSnapshot { get; init; } = null!;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FeatureCapabilitySnapshot ReadySnapshot(
        TrainerFeature feature,
        string groupName = "Test")
    {
        return new FeatureCapabilitySnapshot(
            feature.RawName,
            feature.DisplayName,
            groupName,
            FeatureCapabilityState.Ready,
            "READY",
            "就绪");
    }

    private static FeatureCapabilitySnapshot NonReadySnapshot(
        TrainerFeature feature,
        FeatureCapabilityState state,
        string reasonCode = "NO_TARGET",
        string reason = "test",
        string groupName = "Test")
    {
        return new FeatureCapabilitySnapshot(
            feature.RawName,
            feature.DisplayName,
            groupName,
            state,
            reasonCode,
            reason);
    }

    private static TestCapabilityContext Context(
        FeatureCapabilitySnapshot baseSnapshot,
        Ra3VersionProfile? profile = null,
        IReadOnlyCollection<uint>? installedHooks = null,
        IReadOnlyCollection<uint>? registeredPatchSets = null,
        bool isAgentConnected = true)
    {
        return new TestCapabilityContext
        {
            IsAgentConnected = isAgentConnected,
            CurrentProfile = profile,
            InstalledNativeHookIds = installedHooks ?? Array.Empty<uint>(),
            RegisteredPatchSetIds = registeredPatchSets ?? Array.Empty<uint>(),
            BaseSnapshot = baseSnapshot
        };
    }

    // FrameRateUnlock has NativeToggle with StateId=20, PatchSetId=1, HookId=41.
    private static readonly TrainerFeature FrameRateFeature = new(
        TrainerFeatureIds.FrameRateUnlock60fps, "60fps 帧率解锁", null,
        ["Frame Rate Unlock 60fps"], null, null,
        SupportedProfileIds: ["ra3_1.12"]);

    // SelectedUnitObjectUpgrade is CapabilityOnly with RequiredProfileIds=["ra3_1.12"].
    private static readonly TrainerFeature UnitUpgradeFeature = new(
        TrainerFeatureIds.SelectedUnitObjectUpgrade, "单位升级", null, [], null, null,
        SupportedProfileIds: ["ra3_1.12"], RequiresDirectGameApi: true);

    // Simple toggle (no composite) — Power has StateId=2 only.
    private static readonly TrainerFeature PowerFeature = new(
        TrainerFeatureIds.Power, "电力", null, [], null, null);

    // RawName not present in the behavior catalog.
    private static readonly TrainerFeature UnknownFeature = new(
        "NoSuchFeatureInCatalog", "未知功能", null, [], null, null);

    private static readonly Ra3VersionProfile Ra3112Profile = Ra3VersionProfileRegistry.Ra3112;

    private static readonly Ra3VersionProfile Ra3113Profile = Ra3VersionProfileRegistry.Ra3113;

    // ── Tests ────────────────────────────────────────────────────────────────

    #region Composite NativeToggle

    [Fact]
    public void Evaluate_CompositeToggle_HookMissing_ReturnsUnavailable()
    {
        // FrameRateUnlock composite requires Hook 41 — not present.
        var ctx = Context(
            baseSnapshot: ReadySnapshot(FrameRateFeature),
            profile: Ra3112Profile,
            installedHooks: Array.Empty<uint>(),
            registeredPatchSets: new uint[] { 1 });

        var result = _policy.Evaluate(FrameRateFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("FRAMERATE_COMPOSITE_INCOMPLETE", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_CompositeToggle_PatchSetMissing_ReturnsUnavailable()
    {
        // FrameRateUnlock composite requires PatchSet 1 — not present.
        var ctx = Context(
            baseSnapshot: ReadySnapshot(FrameRateFeature),
            profile: Ra3112Profile,
            installedHooks: new uint[] { 41 },
            registeredPatchSets: Array.Empty<uint>());

        var result = _policy.Evaluate(FrameRateFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("FRAMERATE_COMPOSITE_INCOMPLETE", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_CompositeToggle_AllPresent_ReturnsReady()
    {
        // FrameRateUnlock: both Hook 41 and PatchSet 1 are present.
        var ctx = Context(
            baseSnapshot: ReadySnapshot(FrameRateFeature),
            profile: Ra3112Profile,
            installedHooks: new uint[] { 41 },
            registeredPatchSets: new uint[] { 1 });

        var result = _policy.Evaluate(FrameRateFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Ready, result.State);
        Assert.Equal("READY", result.ReasonCode);
    }

    #endregion

    #region SelectedUnitObjectUpgrade

    [Fact]
    public void Evaluate_SelectedUnitObjectUpgrade_WrongProfile_ReturnsUnavailable()
    {
        // Not ra3_1.12 → UNIT_UPGRADE_PROFILE_NOT_SUPPORTED
        var ctx = Context(
            baseSnapshot: ReadySnapshot(UnitUpgradeFeature),
            profile: Ra3113Profile);

        var result = _policy.Evaluate(UnitUpgradeFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("UNIT_UPGRADE_PROFILE_NOT_SUPPORTED", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_SelectedUnitObjectUpgrade_NativeLayoutMissing_ReturnsUnavailable()
    {
        // ra3_1.12 but with a required NativeAgentRef downgraded to Unsupported
        var profile = Ra3112Profile;
        var modifiedRefs = new Dictionary<string, VersionedAddress>(profile.NativeAgentRefs)
        {
            ["GameObjectAddUpgrade"] = new VersionedAddress(
                "GameObjectAddUpgrade", null, AddressSupportStatus.Unsupported, "test")
        };
        var patchedProfile = new Ra3VersionProfile
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            ProcessName = profile.ProcessName,
            FileVersions = profile.FileVersions,
            Hooks = profile.Hooks,
            RemoteGlobals = profile.RemoteGlobals,
            EngineFunctions = profile.EngineFunctions,
            NativeAgentRefs = modifiedRefs.AsReadOnly(),
            SupportsAgentBackend = profile.SupportsAgentBackend,
            SupportsDirectGameApi = profile.SupportsDirectGameApi,
            OptionalSignatureSymbols = profile.OptionalSignatureSymbols
        };

        var ctx = Context(
            baseSnapshot: ReadySnapshot(UnitUpgradeFeature),
            profile: patchedProfile);

        var result = _policy.Evaluate(UnitUpgradeFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
        Assert.Equal("UNIT_UPGRADE_NATIVE_LAYOUT_UNAVAILABLE", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_SelectedUnitObjectUpgrade_Ready_ReturnsReady()
    {
        // Real ra3_1.12 profile has all three NativeAgentRef entries verified.
        var ctx = Context(
            baseSnapshot: ReadySnapshot(UnitUpgradeFeature),
            profile: Ra3112Profile);

        var result = _policy.Evaluate(UnitUpgradeFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Ready, result.State);
        Assert.Equal("READY", result.ReasonCode);
    }

    #endregion

    #region CapabilityOnly profile requirements

    [Fact]
    public void Evaluate_CapabilityOnly_RequiredProfileMissing_ReturnsUnavailable()
    {
        // SelectedUnitObjectUpgrade requires ra3_1.12; ra3_1.13 → Unavailable
        var ctx = Context(
            baseSnapshot: ReadySnapshot(UnitUpgradeFeature),
            profile: Ra3113Profile);

        var result = _policy.Evaluate(UnitUpgradeFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
    }

    [Fact]
    public void Evaluate_CapabilityOnly_RequiresExactProfile_CandidateProfile_ReturnsUnavailable()
    {
        // SelectedUnitObjectUpgrade has RequiresExactProfile=true and RequiredProfileIds=["ra3_1.12"].
        // A candidate profile with a non-matching ID → Unavailable.
        var ra3112orig = Ra3112Profile;
        var candidate = new Ra3VersionProfile
        {
            Id = "ra3_1.12_sigcompat",
            DisplayName = ra3112orig.DisplayName,
            ProcessName = ra3112orig.ProcessName,
            FileVersions = ra3112orig.FileVersions,
            Hooks = ra3112orig.Hooks,
            RemoteGlobals = ra3112orig.RemoteGlobals,
            EngineFunctions = ra3112orig.EngineFunctions,
            NativeAgentRefs = ra3112orig.NativeAgentRefs,
            SupportsAgentBackend = ra3112orig.SupportsAgentBackend,
            SupportsDirectGameApi = ra3112orig.SupportsDirectGameApi,
            OptionalSignatureSymbols = ra3112orig.OptionalSignatureSymbols
        };

        var ctx = Context(
            baseSnapshot: ReadySnapshot(UnitUpgradeFeature),
            profile: candidate);

        var result = _policy.Evaluate(UnitUpgradeFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
    }

    #endregion

    #region Pass-through

    [Fact]
    public void Evaluate_NoBehaviorEntry_PassesThroughBaseSnapshot()
    {
        // RawName not in behavior catalog → pass through base snapshot.
        var baseSnapshot = NonReadySnapshot(
            UnknownFeature,
            FeatureCapabilityState.Waiting,
            "SOME_REASON",
            "some reason");
        var ctx = Context(baseSnapshot);

        var result = _policy.Evaluate(UnknownFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Waiting, result.State);
        Assert.Equal("SOME_REASON", result.ReasonCode);
        Assert.Equal("some reason", result.Reason);
    }

    [Fact]
    public void Evaluate_NonReadySnapshot_PassesThroughRegardlessOfBehavior()
    {
        // Non-Ready base snapshot should pass through without touching behavior gates.
        var baseSnapshot = NonReadySnapshot(
            FrameRateFeature,
            FeatureCapabilityState.Waiting,
            "NO_TARGET");
        var ctx = Context(
            baseSnapshot,
            profile: Ra3112Profile,
            installedHooks: Array.Empty<uint>());

        var result = _policy.Evaluate(FrameRateFeature, ctx);

        // Even though hooks are missing (would normally trigger composite gate),
        // the non-Ready base snapshot passes through unchanged.
        Assert.Equal(FeatureCapabilityState.Waiting, result.State);
        Assert.Equal("NO_TARGET", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_AgentDisconnected_ReturnsUnavailable()
    {
        // Simulate disconnected state: empty hook/patchset collections with
        // a composite toggle feature. The composite gate catches the missing
        // dependencies rather than a dedicated agent-connected check.
        var ctx = Context(
            baseSnapshot: ReadySnapshot(FrameRateFeature),
            profile: Ra3112Profile,
            installedHooks: Array.Empty<uint>(),
            registeredPatchSets: Array.Empty<uint>(),
            isAgentConnected: false);

        var result = _policy.Evaluate(FrameRateFeature, ctx);

        Assert.Equal(FeatureCapabilityState.Unavailable, result.State);
    }

    #endregion
}
