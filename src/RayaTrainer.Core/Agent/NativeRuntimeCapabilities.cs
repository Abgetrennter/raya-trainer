namespace RayaTrainer.Core.Agent;

[Flags]
public enum NativeRuntimeCapabilities : uint
{
    None = 0,
    GameThreadDispatcher = 1,
    NativeHooks = 2,
    InternalFeatureState = 4,
    Required = GameThreadDispatcher | NativeHooks | InternalFeatureState
}
