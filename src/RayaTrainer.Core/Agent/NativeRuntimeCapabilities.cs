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
    Required = GameThreadDispatcher | NativeHooks | InternalFeatureState | FeatureStateSnapshot | RuntimePatchSets,
    Advertised = Required | ScriptOperationRuntime | InGameOverlay | MatchContext | ProductControlPlane | ReinforcementPresetConsole | SecretProtocolPresetConsole
}
