namespace RayaTrainer.Core.Agent;

/// <summary>
/// Thrown when the per-profile native catalog (game-module RVAs) could not be
/// delivered to the injected Agent DLL within the allotted timeout. The trainer
/// cannot safely install hooks or use Direct GameApi without a valid catalog.
/// </summary>
public sealed class NativeCatalogDeliveryException : AgentCompatibilityException
{
    public NativeCatalogDeliveryException(string message)
        : base(message)
    {
    }

    public NativeCatalogDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
