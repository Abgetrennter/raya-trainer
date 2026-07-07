using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

/// <summary>
/// R2b coverage: queue runners must short-circuit remaining entries when the
/// game stays paused past the grace period, instead of burning the active
/// timeout on each subsequent item.
/// </summary>
public sealed class QueueRunnerPauseTests
{
    private static readonly TrainerFeature SecretProtocolGrantFeature =
        new("Grant Secret Protocol", "授予秘密协议", "F", [], "MustCode+1400", "0x11");

    private static readonly TrainerFeature ReinforcementFeature =
        new("We Need Back", "呼叫战场增援", "J", [], "MustCode2+B00", "0x0C");

    // ---- SecretProtocolQueueRunner ----

    [Fact]
    public async Task SecretProtocol_ShortCircuitsRemainingEntries_WhenPauseGraceExpires()
    {
        var stub = new ControlledResultController(ActionDispatchResult.AbortedDueToPause);
        var entries = new[]
        {
            SecretProtocolEntry("alpha"),
            SecretProtocolEntry("beta"),
            SecretProtocolEntry("gamma")
        };

        var results = await SecretProtocolQueueRunner.ExecuteAsync(
            entries, stub, SecretProtocolGrantFeature,
            TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(1),
            CancellationToken.None,
            pausedGracePeriod: TimeSpan.FromMilliseconds(10));

        // First entry: actually attempted -> AbortedDueToPause.
        // Remaining entries: not attempted, marked AbortedDueToPause by runner.
        Assert.Equal(
            [
                SecretProtocolQueueItemStatus.AbortedDueToPause,
                SecretProtocolQueueItemStatus.AbortedDueToPause,
                SecretProtocolQueueItemStatus.AbortedDueToPause
            ],
            results.Select(r => r.Status));

        // Controller must only have been touched once — proving the runner
        // short-circuited rather than attempting every entry.
        Assert.Equal(1, stub.TriggerActionCallCount);
    }

    [Fact]
    public async Task SecretProtocol_ContinuesAcrossEntries_WhenEachOneConsumes()
    {
        var stub = new ControlledResultController(ActionDispatchResult.Consumed);
        var entries = new[]
        {
            SecretProtocolEntry("alpha"),
            SecretProtocolEntry("beta")
        };

        var results = await SecretProtocolQueueRunner.ExecuteAsync(
            entries, stub, SecretProtocolGrantFeature,
            timeout: null, pollInterval: null,
            CancellationToken.None);

        Assert.Equal(
            [SecretProtocolQueueItemStatus.Executed, SecretProtocolQueueItemStatus.Executed],
            results.Select(r => r.Status));
        Assert.Equal(2, stub.TriggerActionCallCount);
    }

    [Fact]
    public async Task SecretProtocol_ReportsBothExplicitIds_WhenBothAreGranted()
    {
        var stub = new ControlledResultController(ActionDispatchResult.Consumed);
        var entry = new SecretProtocolQueueEntry(new SecretProtocolEntry(
            "test",
            "Allied",
            "Air Power",
            null,
            null,
            null,
            0xDD6C4C5B,
            0x33D87C97));

        var result = Assert.Single(await SecretProtocolQueueRunner.ExecuteAsync(
            [entry], stub, SecretProtocolGrantFeature));

        Assert.Equal("已授予 PlayerTech，并补发关联 Upgrade。", result.Message);
    }

    [Fact]
    public async Task SecretProtocol_ForwardsGracePeriodAndCallback_ToController()
    {
        var grace = TimeSpan.FromSeconds(30);
        var callbackObserved = new List<DispatchWaitStatus>();
        var stub = new ControlledResultController(ActionDispatchResult.Consumed);

        await SecretProtocolQueueRunner.ExecuteAsync(
            [SecretProtocolEntry("only")], stub, SecretProtocolGrantFeature,
            timeout: null, pollInterval: null,
            CancellationToken.None,
            pausedGracePeriod: grace,
            onWaitStatusChanged: callbackObserved.Add);

        Assert.Equal(grace, stub.LastObservedGracePeriod);
    }

    // ---- ReinforcementQueueRunner ----

    [Fact]
    public async Task Reinforcement_ShortCircuitsRemainingEntries_WhenPauseGraceExpires()
    {
        var stub = new ControlledResultController(ActionDispatchResult.AbortedDueToPause);
        var entries = new[]
        {
            new ReinforcementQueueEntry("first", "0x11111111", "2", "1"),
            new ReinforcementQueueEntry("second", "0x22222222", "3", "2"),
            new ReinforcementQueueEntry("third", "0x33333333", "4", "3")
        };

        var results = await ReinforcementQueueRunner.ExecuteAsync(
            entries, stub, ReinforcementFeature,
            TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(1),
            CancellationToken.None,
            pausedGracePeriod: TimeSpan.FromMilliseconds(10));

        Assert.Equal(
            [
                ReinforcementQueueItemStatus.AbortedDueToPause,
                ReinforcementQueueItemStatus.AbortedDueToPause,
                ReinforcementQueueItemStatus.AbortedDueToPause
            ],
            results.Select(r => r.Status));
        Assert.Equal(1, stub.TriggerActionCallCount);
    }

    [Fact]
    public async Task Reinforcement_ContinuesAcrossEntries_WhenEachOneConsumes()
    {
        var stub = new ControlledResultController(ActionDispatchResult.Consumed);
        var entries = new[]
        {
            new ReinforcementQueueEntry("first", "0x11111111", "2", "1"),
            new ReinforcementQueueEntry("second", "0x22222222", "3", "2")
        };

        var results = await ReinforcementQueueRunner.ExecuteAsync(
            entries, stub, ReinforcementFeature,
            timeout: null, pollInterval: null,
            CancellationToken.None);

        Assert.Equal(
            [ReinforcementQueueItemStatus.Executed, ReinforcementQueueItemStatus.Executed],
            results.Select(r => r.Status));
        Assert.Equal(2, stub.TriggerActionCallCount);
    }

    // ---- Transport-cancellation (pipe timeout while game paused) regression ----
    //
    // Real-world scenario: the Agent single-instance pipe's per-command 2 s
    // C# timeout fires while the game is paused. The controller throws
    // OperationCanceledException. The runner must classify that (when the
    // external CT is not the source) as AbortedDueToPause and short-circuit
    // the batch — not Failed-and-continue, which was the bug.

    [Fact]
    public async Task SecretProtocol_ClassifiesTransportCancellation_AsPauseAbort()
    {
        var stub = new TransportCancellingController();
        var entries = new[]
        {
            SecretProtocolEntry("alpha"),
            SecretProtocolEntry("beta"),
            SecretProtocolEntry("gamma")
        };

        var results = await SecretProtocolQueueRunner.ExecuteAsync(
            entries, stub, SecretProtocolGrantFeature,
            timeout: null, pollInterval: null,
            CancellationToken.None,
            pausedGracePeriod: TimeSpan.FromMilliseconds(10));

        Assert.Equal(
            [
                SecretProtocolQueueItemStatus.AbortedDueToPause,
                SecretProtocolQueueItemStatus.AbortedDueToPause,
                SecretProtocolQueueItemStatus.AbortedDueToPause
            ],
            results.Select(r => r.Status));
        Assert.Equal(1, stub.TriggerActionCallCount);
    }

    [Fact]
    public async Task Reinforcement_ClassifiesTransportCancellation_AsPauseAbort()
    {
        var stub = new TransportCancellingController();
        var entries = new[]
        {
            new ReinforcementQueueEntry("first", "0x11111111", "2", "1"),
            new ReinforcementQueueEntry("second", "0x22222222", "3", "2")
        };

        var results = await ReinforcementQueueRunner.ExecuteAsync(
            entries, stub, ReinforcementFeature,
            timeout: null, pollInterval: null,
            CancellationToken.None,
            pausedGracePeriod: TimeSpan.FromMilliseconds(10));

        Assert.Equal(
            [
                ReinforcementQueueItemStatus.AbortedDueToPause,
                ReinforcementQueueItemStatus.AbortedDueToPause
            ],
            results.Select(r => r.Status));
        Assert.Equal(1, stub.TriggerActionCallCount);
    }

    [Fact]
    public async Task Runner_PropagatesExternalCancellation_AsException()
    {
        // When the caller's own CT is cancelled, the runner must NOT swallow
        // it as a pause abort — it must propagate so the caller knows.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var stub = new TransportCancellingController(cts.Token);
        var entries = new[] { SecretProtocolEntry("alpha") };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SecretProtocolQueueRunner.ExecuteAsync(
                entries, stub, SecretProtocolGrantFeature,
                timeout: null, pollInterval: null,
                cts.Token,
                pausedGracePeriod: TimeSpan.FromMilliseconds(10)));
    }

    // ---- Helpers ----

    private static SecretProtocolQueueEntry SecretProtocolEntry(string tech)
        => new(new SecretProtocolEntry(
            Mod: "test",
            Faction: "Allied",
            Name: tech,
            PlayerTech: tech,        // non-null -> PlayerTechId != 0 -> CanGrant
            Upgrade: null,
            SpecialPower: null));

    /// <summary>
    /// Minimal ITrainerFeatureController stub that returns a fixed result from
    /// TriggerActionAndWaitForConsumptionAsync. Only the methods the queue
    /// runners actually call are meaningfully implemented; the rest throw to
    /// fail loud if the contract widens unexpectedly.
    /// </summary>
    private sealed class ControlledResultController : ITrainerFeatureController
    {
        private readonly ActionDispatchResult _result;

        public ControlledResultController(ActionDispatchResult result) => _result = result;

        public int TriggerActionCallCount { get; private set; }
        public TimeSpan? LastObservedGracePeriod { get; private set; }

        public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
            TrainerFeature feature,
            ReinforcementSettings? reinforcementSettings = null,
            TimeSpan? timeout = null,
            TimeSpan? pollInterval = null,
            Action? onDispatched = null,
            CancellationToken cancellationToken = default,
            TimeSpan? pausedGracePeriod = null,
            Action<DispatchWaitStatus>? onWaitStatusChanged = null)
        {
            TriggerActionCallCount++;
            LastObservedGracePeriod = pausedGracePeriod;
            return Task.FromResult(_result);
        }

        public void SetToggle(TrainerFeature feature, bool enabled) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature feature) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings) { }
        public void WriteReinforcementSettings(ReinforcementSettings settings) { }
        public void WriteResourceValues(ResourceValueSettings settings) => throw new NotImplementedException();
        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings) { }
        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings) => throw new NotImplementedException();
        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings) => throw new NotImplementedException();
        public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotImplementedException();
        public void PulseAutoRepair() => throw new NotImplementedException();
        public void ClearAutoRepairPulse() => throw new NotImplementedException();
        public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f) => throw new NotImplementedException();
        public uint ReadSelectedUnitCode() => throw new NotImplementedException();
        public byte ReadActionDispatch() => throw new NotImplementedException();
        public uint ReadGameThreadTick() => throw new NotImplementedException();
        public int ReadGameMode() => throw new NotImplementedException();
        public void Reset(TrainerFeature feature) => throw new NotImplementedException();
        public bool ReadToggleState(TrainerFeature feature) => throw new NotImplementedException();
    }

    /// <summary>
    /// Stub whose TriggerActionAndWaitForConsumptionAsync always throws an
    /// OperationCanceledException tied to a specific CancellationToken. With
    /// default(CancellationToken) it simulates a transport-level timeout
    /// (e.g. Agent single-instance pipe contention); passing a real cancelled
    /// token simulates external cancellation, which the runner must propagate.
    /// </summary>
    private sealed class TransportCancellingController : ITrainerFeatureController
    {
        private readonly CancellationToken _cancelToken;

        public TransportCancellingController(CancellationToken cancelToken = default) => _cancelToken = cancelToken;

        public int TriggerActionCallCount { get; private set; }

        public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
            TrainerFeature feature,
            ReinforcementSettings? reinforcementSettings = null,
            TimeSpan? timeout = null,
            TimeSpan? pollInterval = null,
            Action? onDispatched = null,
            CancellationToken cancellationToken = default,
            TimeSpan? pausedGracePeriod = null,
            Action<DispatchWaitStatus>? onWaitStatusChanged = null)
        {
            TriggerActionCallCount++;
            throw new OperationCanceledException("simulated transport timeout", _cancelToken);
        }

        public void SetToggle(TrainerFeature feature, bool enabled) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature feature) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings) { }
        public void WriteReinforcementSettings(ReinforcementSettings settings) { }
        public void WriteResourceValues(ResourceValueSettings settings) => throw new NotImplementedException();
        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings) { }
        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings) => throw new NotImplementedException();
        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings) => throw new NotImplementedException();
        public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotImplementedException();
        public void PulseAutoRepair() => throw new NotImplementedException();
        public void ClearAutoRepairPulse() => throw new NotImplementedException();
        public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f) => throw new NotImplementedException();
        public uint ReadSelectedUnitCode() => throw new NotImplementedException();
        public byte ReadActionDispatch() => throw new NotImplementedException();
        public uint ReadGameThreadTick() => throw new NotImplementedException();
        public int ReadGameMode() => throw new NotImplementedException();
        public void Reset(TrainerFeature feature) => throw new NotImplementedException();
        public bool ReadToggleState(TrainerFeature feature) => throw new NotImplementedException();
    }
}
