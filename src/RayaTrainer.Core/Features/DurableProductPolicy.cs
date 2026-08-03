using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RayaTrainer.Core.Agent;

namespace RayaTrainer.Core.Features;

/// <summary>
/// Durable product policy store owned by Core/WPF (see
/// <c>docs/architecture/2026-07-22-in-game-product-console.md</c> §4 — the Agent DLL never
/// writes disk config). This model persists ONLY what the frozen wire contract
/// (<c>docs/contracts/product-control-v1.md</c> §10) allows a durable entry to carry:
/// <see cref="DurablePolicyEntry.ProductId"/>, parameters, a symbolic (Rebindable) scope,
/// whether Reapply is enabled and a monotonic <see cref="PolicyRevision"/>.
///
/// It is structurally impossible to store a Captured binding here: the reused
/// <see cref="DurablePolicyEntry"/> record carries no <c>CapturedTarget</c> and no token, and
/// every mutation/import path rejects the captured scopes
/// (<see cref="ScopeKind.SelectedUnit"/>, <see cref="ScopeKind.SelectedObject"/>,
/// <see cref="ScopeKind.FixedPlayer"/>). Captured Target / token / MapEpoch therefore never
/// reach serialization.
///
/// The store is independent of any live agent state: when the Agent is not connected the
/// durable policy is still updated, and no Applied State is fabricated.
/// </summary>
[JsonConverter(typeof(DurableProductPolicyJsonConverter))]
public sealed class DurableProductPolicy
{
    /// <summary>Durable Policy single-import upper bound (contract §3).</summary>
    public const int MaxEntries = 64;

    /// <summary>Per-entry parameter upper bound (contract §3).</summary>
    public const int MaxParametersPerEntry = 16;

    private static readonly ImmutableArray<ScopeKind> SymbolicScopesInternal =
        ImmutableArray.Create(ScopeKind.CurrentPlayer, ScopeKind.AllOtherPlayers, ScopeKind.AllUnits);

    private readonly ImmutableArray<DurablePolicyEntry> _entries;

    private DurableProductPolicy(PolicyRevision revision, ImmutableArray<DurablePolicyEntry> entries)
    {
        Revision = revision;
        _entries = entries;
    }

    /// <summary>An empty policy with revision <c>0</c>.</summary>
    public static DurableProductPolicy Empty { get; } =
        new(new PolicyRevision(0), ImmutableArray<DurablePolicyEntry>.Empty);

    /// <summary>Symbolic (Rebindable) scopes accepted by the durable store.</summary>
    public static IReadOnlyList<ScopeKind> SymbolicScopes => SymbolicScopesInternal;

    /// <summary>Monotonic revision of the durable policy imported by Core (contract §10).</summary>
    public PolicyRevision Revision { get; }

    /// <summary>The persisted entries in insertion order.</summary>
    public IReadOnlyList<DurablePolicyEntry> Entries => _entries;

    /// <summary><c>true</c> when the scope is one of the symbolic Rebindable scopes.</summary>
    public static bool IsSymbolicScope(ScopeKind scope) => SymbolicScopesInternal.Contains(scope);

    /// <summary>
    /// Validates a candidate entry against the durable-policy boundary rules. Returns
    /// <c>null</c> when the entry is storable, otherwise a structured diagnostic describing
    /// the first violation.
    /// </summary>
    public static DurablePolicyDiagnostic? Validate(DurablePolicyEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (entry.PolicyEntryId == 0)
        {
            return new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.MalformedEntry,
                "PolicyEntryId must be non-zero.",
                entry.PolicyEntryId,
                entry.ProductId.Value);
        }

        if (!IsSymbolicScope(entry.ScopeKind))
        {
            return new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.ForbiddenScope,
                $"Scope '{entry.ScopeKind}' is a captured scope and cannot be persisted; " +
                "only CurrentPlayer/AllOtherPlayers/AllUnits are allowed.",
                entry.PolicyEntryId,
                entry.ProductId.Value);
        }

        if (entry.ReapplyPolicy is not (ReapplyPolicy.None or ReapplyPolicy.OnReadyOnce))
        {
            return new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.InvalidReapplyPolicy,
                $"ReapplyPolicy '{entry.ReapplyPolicy}' is not allowed; only None/OnReadyOnce.",
                entry.PolicyEntryId,
                entry.ProductId.Value);
        }

        if (entry.Parameters.Count > MaxParametersPerEntry)
        {
            return new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.TooManyParameters,
                $"Entry has {entry.Parameters.Count} parameters; max is {MaxParametersPerEntry}.",
                entry.PolicyEntryId,
                entry.ProductId.Value);
        }

        return null;
    }

    /// <summary>
    /// Adds or replaces (by <see cref="DurablePolicyEntry.PolicyEntryId"/>) an entry and bumps
    /// the revision monotonically. Throws <see cref="ArgumentException"/> for entries that
    /// violate the boundary rules (captured scope, bad reapply, too many parameters, capacity).
    /// </summary>
    public DurableProductPolicy AddOrUpdate(DurablePolicyEntry entry)
    {
        if (!TryAddOrUpdate(entry, out var updated, out var diagnostic))
        {
            throw new ArgumentException(diagnostic!.Message, nameof(entry));
        }

        return updated;
    }

    /// <summary>
    /// Non-throwing add/update. Returns <c>false</c> and a diagnostic when the entry cannot be
    /// stored; on success returns a new policy with the entry applied and the revision bumped.
    /// </summary>
    public bool TryAddOrUpdate(
        DurablePolicyEntry entry,
        out DurableProductPolicy updated,
        out DurablePolicyDiagnostic? diagnostic)
    {
        updated = this;
        diagnostic = Validate(entry);
        if (diagnostic is not null)
        {
            return false;
        }

        var existingIndex = IndexOf(entry.PolicyEntryId);
        if (existingIndex < 0 && _entries.Length >= MaxEntries)
        {
            diagnostic = new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.TooManyEntries,
                $"Policy already holds {MaxEntries} entries; cannot add a new one.",
                entry.PolicyEntryId,
                entry.ProductId.Value);
            return false;
        }

        var entries = existingIndex >= 0
            ? _entries.SetItem(existingIndex, entry)
            : _entries.Add(entry);

        updated = new DurableProductPolicy(NextRevision(), entries);
        return true;
    }

    /// <summary>
    /// Removes the entry with the given id (if present) and bumps the revision when a change
    /// occurred. Removing a missing id returns the same instance unchanged.
    /// </summary>
    public DurableProductPolicy Remove(ulong policyEntryId)
    {
        var index = IndexOf(policyEntryId);
        if (index < 0)
        {
            return this;
        }

        return new DurableProductPolicy(NextRevision(), _entries.RemoveAt(index));
    }

    /// <summary>
    /// Returns a copy carrying an explicit revision. The revision is guarded monotonic: a
    /// value lower than the current one throws <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public DurableProductPolicy WithRevision(PolicyRevision revision)
    {
        if (revision.Value < Revision.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                $"Revision must not decrease (current {Revision.Value}, requested {revision.Value}).");
        }

        return new DurableProductPolicy(revision, _entries);
    }

    /// <summary>
    /// Imports a batch of candidate entries, isolating any that violate the boundary rules.
    /// Valid entries are kept (deduplicated by id, capped at <see cref="MaxEntries"/>); each
    /// rejected entry yields a diagnostic. The rest of the batch is never discarded.
    /// </summary>
    public static DurableProductPolicy Import(
        ulong revision,
        IEnumerable<DurablePolicyEntry> candidates,
        out IReadOnlyList<DurablePolicyDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var accepted = ImmutableArray.CreateBuilder<DurablePolicyEntry>();
        var seenIds = new HashSet<ulong>();
        var diags = new List<DurablePolicyDiagnostic>();

        foreach (var entry in candidates)
        {
            AppendCandidate(entry, accepted, seenIds, diags);
        }

        diagnostics = diags;
        return new DurableProductPolicy(new PolicyRevision(revision), accepted.ToImmutable());
    }

    private static void AppendCandidate(
        DurablePolicyEntry entry,
        ImmutableArray<DurablePolicyEntry>.Builder accepted,
        HashSet<ulong> seenIds,
        List<DurablePolicyDiagnostic> diags)
    {
        var diagnostic = Validate(entry);
        if (diagnostic is not null)
        {
            diags.Add(diagnostic);
            return;
        }

        if (!seenIds.Add(entry.PolicyEntryId))
        {
            diags.Add(new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.DuplicateEntryId,
                $"Duplicate PolicyEntryId {entry.PolicyEntryId}; keeping the first occurrence.",
                entry.PolicyEntryId,
                entry.ProductId.Value));
            return;
        }

        if (accepted.Count >= MaxEntries)
        {
            diags.Add(new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.TooManyEntries,
                $"Entry dropped; policy import exceeds {MaxEntries} entries.",
                entry.PolicyEntryId,
                entry.ProductId.Value));
            return;
        }

        accepted.Add(entry);
    }

    private int IndexOf(ulong policyEntryId)
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].PolicyEntryId == policyEntryId)
            {
                return i;
            }
        }

        return -1;
    }

    private PolicyRevision NextRevision() => new(Revision.Value + 1);

    // --- JSON projection (contract §10 semantics, clean on-disk shape) ---

    internal static readonly JsonSerializerOptions ScriptValueOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Parses a durable policy from its persisted JSON object, isolating invalid entries with
    /// structured diagnostics. A missing/empty section yields <see cref="Empty"/>.
    /// </summary>
    public static DurableProductPolicy FromJsonElement(
        JsonElement element,
        out IReadOnlyList<DurablePolicyDiagnostic> diagnostics)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics = Array.Empty<DurablePolicyDiagnostic>();
            return Empty;
        }

        ulong revision = 0;
        if (element.TryGetProperty("Revision", out var revEl) &&
            revEl.ValueKind == JsonValueKind.Number &&
            revEl.TryGetUInt64(out var parsedRevision))
        {
            revision = parsedRevision;
        }

        var diags = new List<DurablePolicyDiagnostic>();
        var candidates = new List<DurablePolicyEntry>();
        if (element.TryGetProperty("Entries", out var entriesEl) &&
            entriesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entriesEl.EnumerateArray())
            {
                var entry = ParseEntry(item, diags);
                if (entry is not null)
                {
                    candidates.Add(entry);
                }
            }
        }

        var policy = Import(revision, candidates, out var importDiags);
        diags.AddRange(importDiags);
        diagnostics = diags;
        return policy;
    }

    private static DurablePolicyEntry? ParseEntry(JsonElement item, List<DurablePolicyDiagnostic> diags)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            diags.Add(new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.MalformedEntry, "Entry is not a JSON object.", 0, null));
            return null;
        }

        var productIdText = item.TryGetProperty("ProductId", out var pidEl) && pidEl.ValueKind == JsonValueKind.String
            ? pidEl.GetString()
            : null;

        ulong policyEntryId = 0;
        if (item.TryGetProperty("PolicyEntryId", out var idEl) &&
            idEl.ValueKind == JsonValueKind.Number)
        {
            idEl.TryGetUInt64(out policyEntryId);
        }

        ProductId productId;
        try
        {
            productId = new ProductId(productIdText ?? string.Empty);
        }
        catch (ArgumentException)
        {
            diags.Add(new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.InvalidProductId,
                $"ProductId '{productIdText}' is invalid.",
                policyEntryId,
                productIdText));
            return null;
        }

        var scopeText = item.TryGetProperty("Scope", out var scopeEl) && scopeEl.ValueKind == JsonValueKind.String
            ? scopeEl.GetString()
            : null;
        if (!Enum.TryParse<ScopeKind>(scopeText, ignoreCase: true, out var scope))
        {
            diags.Add(new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.ForbiddenScope,
                $"Scope '{scopeText}' is not a recognized symbolic scope.",
                policyEntryId,
                productIdText));
            return null;
        }

        var reapplyText = item.TryGetProperty("Reapply", out var reEl) && reEl.ValueKind == JsonValueKind.String
            ? reEl.GetString()
            : null;
        if (!Enum.TryParse<ReapplyPolicy>(reapplyText, ignoreCase: true, out var reapply))
        {
            diags.Add(new DurablePolicyDiagnostic(
                DurablePolicyDiagnosticCode.InvalidReapplyPolicy,
                $"ReapplyPolicy '{reapplyText}' is not recognized.",
                policyEntryId,
                productIdText));
            return null;
        }

        var parameters = new List<ScriptValue>();
        if (item.TryGetProperty("Parameters", out var paramsEl) &&
            paramsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in paramsEl.EnumerateArray())
            {
                try
                {
                    parameters.Add(p.Deserialize<ScriptValue>(ScriptValueOptions));
                }
                catch (JsonException)
                {
                    diags.Add(new DurablePolicyDiagnostic(
                        DurablePolicyDiagnosticCode.MalformedEntry,
                        "Entry has a malformed parameter value.",
                        policyEntryId,
                        productIdText));
                    return null;
                }
            }
        }

        return new DurablePolicyEntry(policyEntryId, productId, scope, reapply, parameters);
    }

    internal void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Revision", Revision.Value);
        writer.WriteStartArray("Entries");
        foreach (var entry in _entries)
        {
            writer.WriteStartObject();
            writer.WriteNumber("PolicyEntryId", entry.PolicyEntryId);
            writer.WriteString("ProductId", entry.ProductId.Value);
            writer.WriteString("Scope", entry.ScopeKind.ToString());
            writer.WriteString("Reapply", entry.ReapplyPolicy.ToString());
            writer.WritePropertyName("Parameters");
            JsonSerializer.Serialize(writer, entry.Parameters, ScriptValueOptions);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

/// <summary>Category of a durable-policy validation/isolation failure.</summary>
public enum DurablePolicyDiagnosticCode
{
    InvalidProductId,
    ForbiddenScope,
    InvalidReapplyPolicy,
    TooManyParameters,
    TooManyEntries,
    DuplicateEntryId,
    MalformedEntry,
}

/// <summary>Structured diagnostic emitted when a durable-policy entry is rejected/isolated.</summary>
public sealed record DurablePolicyDiagnostic(
    DurablePolicyDiagnosticCode Code,
    string Message,
    ulong PolicyEntryId,
    string? ProductId);

/// <summary>Serializes <see cref="DurableProductPolicy"/> to a clean, isolating JSON shape.</summary>
public sealed class DurableProductPolicyJsonConverter : JsonConverter<DurableProductPolicy>
{
    public override DurableProductPolicy Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return DurableProductPolicy.FromJsonElement(document.RootElement, out _);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DurableProductPolicy value,
        JsonSerializerOptions options)
    {
        (value ?? DurableProductPolicy.Empty).WriteJson(writer, options);
    }
}
