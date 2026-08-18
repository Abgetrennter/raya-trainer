namespace RayaTrainer.Core.Agent;

[Flags]
public enum NativeRuntimeCapabilities : uint
{
    None = 0,
    GameThreadDispatcher = 1,
    NativeHooks = 2,
    InternalFeatureState = 4,
    FeatureStateSnapshot = 8,
    RuntimePatchSets = 16,
    ScriptOperationRuntime = 32,
    InGameOverlay = 64,
    MatchContext = 128,
    ProductControlPlane = 256,
    ReinforcementPresetConsole = 512,
    SecretProtocolPresetConsole = 1024,
    // Additive/optional: the versioned AttributeModifier bundle is loaded once per process on the
    // game thread (AgentCommand.LoadBundledRuntimeAssets). Products depending on it fail-closed when
    // unavailable; not in Required, negotiated via capability within v13.
    RuntimeAssetInjection = 2048,
    Required = GameThreadDispatcher | NativeHooks | InternalFeatureState | FeatureStateSnapshot | RuntimePatchSets,
    Advertised = Required | ScriptOperationRuntime | InGameOverlay | MatchContext | ProductControlPlane | ReinforcementPresetConsole | SecretProtocolPresetConsole | RuntimeAssetInjection
}
