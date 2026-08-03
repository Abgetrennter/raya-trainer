namespace RayaTrainer.Core.Agent;

/// <summary>
/// Thrown when the injected Agent DLL is incompatible with the current trainer build
/// (protocol version mismatch, build fingerprint mismatch, missing native capabilities,
/// or signature-compatibility validation failure).
/// </summary>
public class AgentCompatibilityException : InvalidOperationException
{
    public AgentCompatibilityException(string message)
        : base(message)
    {
    }

    public AgentCompatibilityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
