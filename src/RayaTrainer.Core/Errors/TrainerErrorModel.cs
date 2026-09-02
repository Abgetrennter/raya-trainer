namespace RayaTrainer.Core.Errors;

/// <summary>
/// Origin domain of an error — the primary classification axis of the unified error
/// vocabulary (design doc §3.1). Drives string-id prefix, diagnostic stage attribution
/// and HTTP status mapping; wire protocol enums keep their own numeric codes.
/// </summary>
public enum ErrorDomain
{
    /// <summary>Environment and connectivity: process detection, injection, pipe loss, runtime prerequisites.</summary>
    Env,

    /// <summary>Contract and version: protocol/schema/fingerprint mismatch, asset hash contracts, signature resolution.</summary>
    Contract,

    /// <summary>Request and state: invalid parameters, queue full, wrong timing, capability disabled, invalid target.</summary>
    Request,

    /// <summary>Execution: hook/patch failure, memory writes, dispatch timeout, effect not observed, compensation failure.</summary>
    Execution,

    /// <summary>Internal fault: invariant violations, malformed payloads, unexpected exceptions — bugs, fail loudly.</summary>
    Fault,
}

/// <summary>
/// Disposition hint for a failure — drives UI next-step wording and diagnostic stage
/// action buttons (design doc §3.3).
/// </summary>
public enum RetryHint
{
    /// <summary>Retryable as-is, e.g. queue full — try again on the next tick.</summary>
    Retryable,

    /// <summary>The user must act first, e.g. enter a match or connect the Agent.</summary>
    UserAction,

    /// <summary>Retrying cannot help, e.g. contract mismatch requires an update.</summary>
    NotRetryable,
}

/// <summary>
/// Single base class for all trainer-owned exceptions (design doc §4.2). Expected
/// business failures travel as structured results, not exceptions; this type is reserved
/// for failures that must unwind — invariant violations and domain errors with no
/// structured channel. Classification lives in properties, not in a subclass hierarchy.
/// </summary>
public class TrainerException : Exception
{
    public TrainerException(
        ErrorDomain domain,
        string errorCode,
        RetryHint retryHint,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        Domain = domain;
        ErrorCode = errorCode;
        RetryHint = retryHint;
    }

    public ErrorDomain Domain { get; }

    /// <summary>Stable string id, <c>&lt;domain&gt;.&lt;name&gt;</c> form (append-only, never renamed).</summary>
    public string ErrorCode { get; }

    public RetryHint RetryHint { get; }
}

/// <summary>
/// Structured failure record for expected-failure channels (design doc §4.3). Protocol
/// surfaces keep returning their domain enums; boundaries translate them into this shape
/// via the classification mapping. <see cref="Stage"/> preserves the layered provenance
/// (product plane: admission/execution/effect/compensation) and <see cref="Evidence"/>
/// carries machine-readable coordinates (symbol names, RVAs, hook ids) — Message is human
/// narration only and never carries coordinates.
/// </summary>
public readonly record struct TrainerError(
    ErrorDomain Domain,
    string Code,
    string Message,
    RetryHint RetryHint,
    string? Stage = null,
    IReadOnlyDictionary<string, string>? Evidence = null);
