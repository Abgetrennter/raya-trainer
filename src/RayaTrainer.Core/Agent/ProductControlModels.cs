using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Product control plane v1 managed models. Mirrors the frozen wire contract
/// <c>docs/contracts/product-control-v1.md</c> (sections 2, 5, 11). Wire values are
/// authoritative; these enums and records must stay byte-for-byte compatible with the
/// <c>ProductControlWireCodec</c> and the C0 golden fixtures.
/// </summary>

// --- Semantic identity (section 2) ---

/// <summary>
/// Canonical product definition name. Matches <c>[a-z0-9][a-z0-9._-]{0,95}</c> and is at
/// most 96 UTF-8 bytes. The grammar is all-ASCII so byte length and char length agree.
/// </summary>
public readonly record struct ProductId
{
    private static readonly Regex Pattern =
        new("^[a-z0-9][a-z0-9._-]{0,95}$", RegexOptions.Compiled);

    public string Value { get; }

    public ProductId(string value)
    {
        if (value is null)
        {
            throw new ArgumentException("ProductId value is null.", nameof(value));
        }

        if (!Pattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"ProductId '{value}' does not match [a-z0-9][a-z0-9._-]{{0,95}}.",
                nameof(value));
        }

        if (Encoding.UTF8.GetByteCount(value) > ProductControlWireCodec.MaxProductIdBytes)
        {
            throw new ArgumentException(
                $"ProductId exceeds {ProductControlWireCodec.MaxProductIdBytes} UTF-8 bytes.",
                nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Agent-allocated intent identity within one session. <c>0</c> is invalid.</summary>
public readonly record struct IntentId(ulong Value)
{
    public bool IsInvalid => Value == 0;

    public override string ToString() => Value.ToString();
}

/// <summary>Monotonic revision of the durable product policy imported by Core.</summary>
public readonly record struct PolicyRevision(ulong Value)
{
    public bool IsEmpty => Value == 0;
}

/// <summary>Monotonic epoch of one usable match context. <c>0</c> means unavailable.</summary>
public readonly record struct MapEpoch(ulong Value)
{
    public bool IsUnavailable => Value == 0;
}

/// <summary>Immutable context snapshot publication generation.</summary>
public readonly record struct SnapshotGeneration(ulong Value);

/// <summary>Script/Operation catalog owned-memory generation.</summary>
public readonly record struct ScriptCatalogGeneration(ulong Value);

// --- Context binding enums (section 5.1) ---

public enum BindingKind : byte
{
    Live = 1,
    Rebindable = 2,
    Captured = 3,
}

public enum ScopeKind : byte
{
    CurrentPlayer = 1,
    AllOtherPlayers = 2,
    AllUnits = 3,
    SelectedUnit = 4,
    SelectedObject = 5,
    FixedPlayer = 6,
}

public enum ReapplyPolicy : byte
{
    None = 0,
    OnReadyOnce = 1,
}

// --- Match context enums (section 6) ---

public enum MatchLifecycle : byte
{
    Unavailable = 1,
    Loading = 2,
    Resolving = 3,
    Ready = 4,
    Invalidated = 5,
}

public enum SinglePlayerProof : byte
{
    Unknown = 0,
    Proven = 1,
}

[Flags]
public enum RuntimeFlags : byte
{
    None = 0,
    Script = 0x01,
    Native = 0x02,
    Overlay = 0x04,
}

[Flags]
public enum ScopeMask : byte
{
    None = 0,
    CurrentPlayer = 0x01,
    AllOtherPlayers = 0x02,
    AllUnits = 0x04,
    SelectionSummary = 0x08,
}

// --- Intent / result enums (sections 7, 8, 9) ---

public enum ProductAcceptance : byte
{
    Accepted = 1,
    Rejected = 2,
}

public enum AdmissionState : byte
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    Superseded = 4,
    Expired = 5,
}

public enum ExecutionState : byte
{
    NotStarted = 0,
    Running = 1,
    Executed = 2,
    Failed = 3,
}

public enum EffectState : byte
{
    NotApplicable = 0,
    Unknown = 1,
    Observed = 2,
    NotObserved = 3,
}

public enum CompensationState : byte
{
    NotRequired = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
}

public enum ResultAvailability : byte
{
    Present = 1,
    Expired = 2,
    UnknownIntent = 3,
}

public enum DesiredState : byte
{
    Pending = 1,
    Active = 2,
    Disabled = 3,
    Superseded = 4,
}

public enum ProductErrorCode : ushort
{
    None = 0,
    InvalidRequest = 1,
    CapabilityUnavailable = 2,
    ContextUnavailable = 3,
    NotSinglePlayer = 4,
    QueueFull = 5,
    ResultExpired = 6,
    ProductUnavailable = 7,
    SchemaMismatch = 8,
    CapturedTargetInvalid = 9,
    MapEpochMismatch = 10,
    TargetRevisionMismatch = 11,
    DependencyConflict = 12,
    PolicyStale = 13,
    ExecutionFault = 14,
    UnsupportedBinding = 15,
    Superseded = 16,
    InternalError = 17,
}

// --- Context binding types (section 5) ---

/// <summary>
/// Agent-owned context token. Opaque bytes the client stores and carries verbatim. Wire
/// shape: <c>u8 tokenKind, u8 reserved=0, u16 tokenLength (1..64), bytes</c>. A zero
/// <see cref="TokenKind"/> with an empty token represents "no token" in the context query
/// response. This token is used only for symbolic CurrentPlayer context; Captured object
/// bindings use <see cref="CapturedTarget.ObjectIds"/> instead.
/// </summary>
public sealed record AgentOwnedToken(
    byte TokenKind,
    ImmutableArray<byte> Token)
{
    public int Length => Token.Length;

    // ImmutableArray<byte>.Equals is reference-based; two tokens with identical bytes must
    // compare equal so that records carrying a token round-trip via value equality.
    public bool Equals(AgentOwnedToken? other) =>
        other is not null
        && TokenKind == other.TokenKind
        && Token.AsSpan().SequenceEqual(other.Token.AsSpan());

    public override int GetHashCode() => HashCode.Combine(TokenKind, Token.Length);
}

/// <summary>
/// Payload carried only by <see cref="BindingKind.Captured"/> object bindings.
/// Object IDs are engine identities captured at submission time. The Agent resolves
/// every ID again on the game thread before use; no native pointer is retained.
/// </summary>
public sealed record CapturedTarget(ImmutableArray<uint> ObjectIds)
{
    // ImmutableArray<T>.Equals is reference-based. Captured target equality is part of
    // request round-trip tests and scope-key semantics, so compare the bounded ID list.
    public bool Equals(CapturedTarget? other) =>
        other is not null && ObjectIds.AsSpan().SequenceEqual(other.ObjectIds.AsSpan());

    public override int GetHashCode() => ObjectIds.Length;
}

/// <summary>
/// Resolved context binding. The binding/scope/reapply combination must obey section 5.1:
/// Live only allows <see cref="ScopeKind.CurrentPlayer"/>; Rebindable only allows the
/// symbolic scopes; Captured only allows the captured scopes with
/// <see cref="ReapplyPolicy.None"/>.
/// </summary>
public sealed record ContextBinding(
    BindingKind Kind,
    ScopeKind Scope,
    ReapplyPolicy Reapply,
    CapturedTarget? Captured = null);

// --- Command 57: Query Match Context ---

public sealed record QueryContextRequest(
    ScopeMask RequestedScopeMask,
    SnapshotGeneration KnownSnapshotGeneration);

public sealed record QueryContextResponse(
    ushort AgentStatusCode,
    MatchLifecycle Lifecycle,
    SinglePlayerProof SinglePlayerProof,
    RuntimeFlags RuntimeFlags,
    ScopeMask ScopeAvailabilityMask,
    uint ActivePlayerCount,
    MapEpoch MapEpoch,
    SnapshotGeneration SnapshotGeneration,
    ScriptCatalogGeneration ScriptCatalogGeneration,
    AgentOwnedToken? CurrentPlayerToken);

// --- Command 58: Submit Product Intent ---

public sealed record SubmitIntentRequest(
    ProductId ProductId,
    ContextBinding Binding,
    IReadOnlyList<ScriptValue> Parameters);

public sealed record SubmitIntentResponse(
    ushort AgentStatusCode,
    ProductAcceptance Acceptance,
    ProductErrorCode ErrorCode,
    IntentId IntentId);

// --- Command 59: Get Product Result ---

public sealed record GetResultRequest(IntentId IntentId);

/// <summary>
/// Layered product result (admission / execution / effect / compensation / evidence).
/// The distinct state enums are kept separate on purpose: Accepted, Dispatched and
/// EffectUnknown must never collapse into "effect applied". When
/// <see cref="Availability"/> is not <see cref="ResultAvailability.Present"/>, the body
/// fields carry defaults and <see cref="ProductId"/>/<see cref="Evidence"/> are empty.
/// </summary>
public sealed record ProductResult(
    ushort AgentStatusCode,
    ResultAvailability Availability,
    AdmissionState Admission,
    ExecutionState Execution,
    EffectState Effect,
    CompensationState Compensation,
    ProductErrorCode ErrorCode,
    IntentId IntentId,
    MapEpoch MapEpoch,
    ProductId? ProductId,
    string Detail,
    IReadOnlyList<ScriptValue> Evidence);

// --- Command 60: Get Desired Intents ---

public sealed record GetDesiredRequest(
    uint Offset,
    uint Limit,
    PolicyRevision KnownPolicyRevision);

public sealed record DesiredIntentSummary(
    IntentId IntentId,
    ProductId ProductId,
    BindingKind BindingKind,
    ScopeKind ScopeKind,
    ReapplyPolicy ReapplyPolicy,
    DesiredState DesiredState,
    MapEpoch LastMapEpoch);

public sealed record GetDesiredResponse(
    ushort AgentStatusCode,
    PolicyRevision PolicyRevision,
    uint TotalCount,
    IReadOnlyList<DesiredIntentSummary> Items);

// --- Command 61: Apply Durable Product Policy ---

/// <summary>
/// One durable policy entry. Captured scopes, tokens and a fixed PlayerIndex are
/// forbidden here: <see cref="ScopeKind"/> is restricted to the symbolic scopes and the
/// entry implicitly behaves like a Rebindable binding.
/// </summary>
public sealed record DurablePolicyEntry(
    ulong PolicyEntryId,
    ProductId ProductId,
    ScopeKind ScopeKind,
    ReapplyPolicy ReapplyPolicy,
    IReadOnlyList<ScriptValue> Parameters);

public sealed record ApplyPolicyRequest(
    PolicyRevision PolicyRevision,
    IReadOnlyList<DurablePolicyEntry> Entries);

/// <summary>Per-entry import outcome. <see cref="IntentId"/> is <c>0</c> on failure.</summary>
public sealed record DurablePolicyImportResult(
    ulong PolicyEntryId,
    IntentId IntentId,
    ProductErrorCode ErrorCode);

public sealed record ApplyPolicyResponse(
    ushort AgentStatusCode,
    PolicyRevision PolicyRevision,
    uint AcceptedCount,
    uint RejectedCount,
    IReadOnlyList<DurablePolicyImportResult> Results);
