using RayaTrainer.Core.Errors;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Thrown when the injected Agent DLL is incompatible with the current trainer build
/// (protocol version mismatch, build fingerprint mismatch, missing native capabilities,
/// or signature-compatibility validation failure).
/// </summary>
public class AgentCompatibilityException : TrainerException
{
    public const string Code = "CONTRACT.AGENT_INCOMPATIBLE";

    public AgentCompatibilityException(string message)
        : base(ErrorDomain.Contract, Code, RetryHint.NotRetryable, message)
    {
    }

    public AgentCompatibilityException(string message, Exception innerException)
        : base(ErrorDomain.Contract, Code, RetryHint.NotRetryable, message, innerException)
    {
    }
}
