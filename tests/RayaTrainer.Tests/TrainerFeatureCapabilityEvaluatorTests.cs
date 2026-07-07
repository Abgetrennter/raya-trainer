using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerFeatureCapabilityEvaluatorTests
{
    private static readonly TrainerFeature StandardFeature =
        new("Zoom", "无限缩放", null, ["iEnable+F"], null, null);

    private static readonly TrainerFeature AgentFeature =
        new("Agent Action", "Agent 操作", null, [], null, null, RequiresDirectGameApi: true);

    [Fact]
    public void NoTargetIsWaitingBeforeBackendChecks()
    {
        var capability = Evaluate(AgentFeature, hasTarget: false);

        Assert.Equal(FeatureCapabilityState.Waiting, capability.State);
        Assert.Equal("NO_TARGET", capability.ReasonCode);
    }

    [Fact]
    public void AttachedSessionWithoutPatchesIsWaiting()
    {
        var capability = Evaluate(
            StandardFeature,
            hasTarget: true,
            sessionReady: true);

        Assert.Equal(FeatureCapabilityState.Waiting, capability.State);
        Assert.Equal("PATCH_NOT_INSTALLED", capability.ReasonCode);
    }

    [Fact]
    public void DirectGameApiFeatureIsUnavailableWithoutNativeCapability()
    {
        var capability = Evaluate(
            AgentFeature,
            hasTarget: true,
            sessionReady: true);

        Assert.Equal(FeatureCapabilityState.Unavailable, capability.State);
        Assert.Equal("DIRECT_GAME_API_REQUIRED", capability.ReasonCode);
    }

    [Fact]
    public void ExplicitSafetyDisableWinsEvenAfterTargetDisappears()
    {
        var capability = Evaluate(
            StandardFeature,
            unavailableReason: "hook 已安全跳过");

        Assert.Equal(FeatureCapabilityState.Unavailable, capability.State);
        Assert.Equal("PROFILE_OR_HOOK_UNAVAILABLE", capability.ReasonCode);
        Assert.Equal("hook 已安全跳过", capability.Reason);
    }

    [Fact]
    public void AgentControllerMustBeReadyAfterPatchInstallation()
    {
        var capability = Evaluate(
            AgentFeature,
            hasTarget: true,
            sessionReady: true,
            patchesInstalled: true,
            backendSupportsDirectGameApi: true);

        Assert.Equal(FeatureCapabilityState.Unavailable, capability.State);
        Assert.Equal("DIRECT_GAME_API_NOT_READY", capability.ReasonCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SatisfiedSessionIsReady(bool requiresDirectGameApi)
    {
        var feature = requiresDirectGameApi ? AgentFeature : StandardFeature;
        var capability = Evaluate(
            feature,
            hasTarget: true,
            sessionReady: true,
            patchesInstalled: true,
            backendSupportsDirectGameApi: true,
            directGameApiReady: true);

        Assert.Equal(FeatureCapabilityState.Ready, capability.State);
        Assert.Equal("READY", capability.ReasonCode);
    }

    private static FeatureCapabilitySnapshot Evaluate(
        TrainerFeature feature,
        bool hasTarget = false,
        bool sessionReady = false,
        bool patchesInstalled = false,
        bool backendSupportsDirectGameApi = false,
        bool directGameApiReady = false,
        string? unavailableReason = null) =>
        TrainerFeatureCapabilityEvaluator.Evaluate(
            feature,
            new TrainerFeatureCapabilityContext(
                hasTarget,
                sessionReady,
                patchesInstalled,
                backendSupportsDirectGameApi,
                directGameApiReady,
                unavailableReason));
}
