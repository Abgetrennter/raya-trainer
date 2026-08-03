using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Wire codec for the frozen Product Control Plane v1 contract
/// (<c>docs/contracts/product-control-v1.md</c>, commands 57-61). Every byte sequence
/// produced and consumed here must match the C0 golden fixtures in
/// <c>tests/fixtures/product-control-v1/*.hex</c> exactly. The <see cref="ScriptValue"/>
/// encoding uses the frozen product-control value format (u8 kind, u32
/// bodyLength, body; little-endian int64/double; boolean 0/1; strict UTF-8 strings); the
/// model type is reused, no second value model is introduced.
/// </summary>
public static class ProductControlWireCodec
{
    public const ushort SchemaVersion = 2;

    public const ushort AgentStatusOk = 0;

    // Bounded sizes from contract section 3.
    public const int MaxProductIdBytes = 96;
    public const int MaxGenericStringBytes = 4096;
    public const int MaxContextTokenBytes = 64;
    public const int MaxCapturedObjectIds = 4096;
    public const int MaxIntentParameters = 16;
    public const int MaxPolicyEntries = 64;
    public const int MaxIntentQueue = 32;
    public const int MaxResultRetentionWindow = 64;
    public const int MaxDesiredRegistry = 64;
    public const int MaxEvidenceValues = 16;
    public const int MaxDetailBytes = 256;
    public const int MaxContextQueryPayloadBytes = 65_536;
    public const int MaxGetDesiredLimit = 64;

    private const byte RuntimeFlagsMask = (byte)(RuntimeFlags.Script | RuntimeFlags.Native | RuntimeFlags.Overlay);
    private const byte ScopeMaskMask = (byte)(
        ScopeMask.CurrentPlayer | ScopeMask.AllOtherPlayers | ScopeMask.AllUnits | ScopeMask.SelectionSummary);

    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    // ---------- Command 57: Query Match Context ----------

    public static byte[] EncodeQueryContextRequest(QueryContextRequest request)
    {
        ValidateScopeMask(request.RequestedScopeMask);
        using var stream = new MemoryStream();
        WriteUInt16(stream, SchemaVersion);
        stream.WriteByte((byte)request.RequestedScopeMask);
        WriteReservedByte(stream);
        WriteUInt64(stream, request.KnownSnapshotGeneration.Value);
        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static QueryContextRequest DecodeQueryContextRequest(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        reader.EnsureSchemaVersion();
        var mask = ReadScopeMask(ref reader);
        reader.RequireReservedByte();
        var generation = new SnapshotGeneration(reader.ReadUInt64());
        reader.RequireEnd();
        return new QueryContextRequest(mask, generation);
    }

    public static byte[] EncodeQueryContextResponse(QueryContextResponse response)
    {
        ValidateQueryContextResponse(response);
        using var stream = new MemoryStream();
        WriteResponsePrefix(stream, response.AgentStatusCode);
        if (response.AgentStatusCode != AgentStatusOk)
        {
            EnsurePayloadLimit(stream.Length);
            return stream.ToArray();
        }

        stream.WriteByte((byte)response.Lifecycle);
        stream.WriteByte((byte)response.SinglePlayerProof);
        stream.WriteByte((byte)response.RuntimeFlags);
        stream.WriteByte((byte)response.ScopeAvailabilityMask);
        WriteUInt32(stream, response.ActivePlayerCount);
        WriteUInt64(stream, response.MapEpoch.Value);
        WriteUInt64(stream, response.SnapshotGeneration.Value);
        WriteUInt64(stream, response.ScriptCatalogGeneration.Value);
        WriteOptionalToken(stream, response.CurrentPlayerToken);
        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static QueryContextResponse DecodeQueryContextResponse(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        var (agentStatus, schema) = reader.ReadResponsePrefix();
        if (agentStatus != AgentStatusOk)
        {
            reader.RequireEnd();
            return EmptyQueryContextResponse(agentStatus);
        }

        var lifecycle = ReadEnum<MatchLifecycle>(ref reader);
        var proof = ReadEnum<SinglePlayerProof>(ref reader);
        var flags = ReadRuntimeFlags(ref reader);
        var mask = ReadScopeMask(ref reader);
        var activePlayerCount = reader.ReadUInt32();
        var mapEpoch = new MapEpoch(reader.ReadUInt64());
        var snapshot = new SnapshotGeneration(reader.ReadUInt64());
        var catalog = new ScriptCatalogGeneration(reader.ReadUInt64());
        var token = ReadOptionalToken(ref reader);
        reader.RequireEnd();
        return new QueryContextResponse(
            agentStatus,
            lifecycle,
            proof,
            flags,
            mask,
            activePlayerCount,
            mapEpoch,
            snapshot,
            catalog,
            token);
    }

    // ---------- Command 58: Submit Product Intent ----------

    public static byte[] EncodeSubmitIntentRequest(SubmitIntentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        ValidateContextBinding(request.Binding, allowCaptured: true);
        ValidateParameterCount(request.Parameters.Count);
        using var stream = new MemoryStream();
        WriteUInt16(stream, SchemaVersion);
        WriteProductId(stream, request.ProductId);
        WriteBindingHeader(stream, request.Binding);
        WriteUInt16(stream, checked((ushort)request.Parameters.Count));
        WriteReservedUInt16(stream);
        if (request.Binding.Kind == BindingKind.Captured)
        {
            WriteCapturedBlock(stream, request.Binding.Captured!);
        }

        foreach (var parameter in request.Parameters)
        {
            WriteScriptValue(stream, parameter);
        }

        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static SubmitIntentRequest DecodeSubmitIntentRequest(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        reader.EnsureSchemaVersion();
        var productId = reader.ReadProductId();
        var binding = ReadBinding(ref reader);
        var parameterCount = reader.ReadUInt16();
        if (parameterCount > MaxIntentParameters)
        {
            throw new InvalidDataException(
                $"Product intent parameter count {parameterCount} exceeds {MaxIntentParameters}.");
        }

        reader.RequireReservedUInt16();
        CapturedTarget? captured = null;
        if (binding.Kind == BindingKind.Captured)
        {
            captured = ReadCapturedBlock(ref reader);
            binding = binding with { Captured = captured };
        }

        ValidateContextBinding(binding, allowCaptured: true);
        var parameters = new ScriptValue[parameterCount];
        for (var index = 0; index < parameterCount; index++)
        {
            parameters[index] = reader.ReadScriptValue();
        }

        reader.RequireEnd();
        return new SubmitIntentRequest(productId, binding, parameters);
    }

    public static byte[] EncodeSubmitIntentResponse(SubmitIntentResponse response)
    {
        ValidateSubmitIntentResponse(response);

        using var stream = new MemoryStream();
        WriteResponsePrefix(stream, response.AgentStatusCode);
        if (response.AgentStatusCode != AgentStatusOk)
        {
            EnsurePayloadLimit(stream.Length);
            return stream.ToArray();
        }

        stream.WriteByte((byte)response.Acceptance);
        WriteReservedByte(stream);
        WriteUInt16(stream, (ushort)response.ErrorCode);
        WriteUInt64(stream, response.IntentId.Value);
        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static SubmitIntentResponse DecodeSubmitIntentResponse(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        var (agentStatus, schema) = reader.ReadResponsePrefix();
        if (agentStatus != AgentStatusOk)
        {
            reader.RequireEnd();
            return new SubmitIntentResponse(agentStatus, ProductAcceptance.Rejected, ProductErrorCode.None, default);
        }

        var acceptance = ReadEnum<ProductAcceptance>(ref reader);
        reader.RequireReservedByte();
        var errorCode = ReadErrorCode(ref reader);
        var intentId = new IntentId(reader.ReadUInt64());
        reader.RequireEnd();
        var response = new SubmitIntentResponse(agentStatus, acceptance, errorCode, intentId);
        ValidateSubmitIntentResponse(response);
        return response;
    }

    // ---------- Command 59: Get Product Result ----------

    public static byte[] EncodeGetResultRequest(GetResultRequest request)
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, SchemaVersion);
        WriteUInt64(stream, request.IntentId.Value);
        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static GetResultRequest DecodeGetResultRequest(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        reader.EnsureSchemaVersion();
        var intentId = new IntentId(reader.ReadUInt64());
        reader.RequireEnd();
        return new GetResultRequest(intentId);
    }

    public static byte[] EncodeProductResult(ProductResult result)
    {
        ValidateProductResult(result);
        using var stream = new MemoryStream();
        WriteResponsePrefix(stream, result.AgentStatusCode);
        if (result.AgentStatusCode != AgentStatusOk)
        {
            EnsurePayloadLimit(stream.Length);
            return stream.ToArray();
        }

        stream.WriteByte((byte)result.Availability);
        stream.WriteByte((byte)result.Admission);
        stream.WriteByte((byte)result.Execution);
        stream.WriteByte((byte)result.Effect);
        stream.WriteByte((byte)result.Compensation);
        WriteReservedByte(stream);
        WriteUInt16(stream, (ushort)result.ErrorCode);
        WriteUInt64(stream, result.IntentId.Value);
        WriteUInt64(stream, result.MapEpoch.Value);
        WriteOptionalProductId(stream, result.ProductId);
        WriteBoundedString(stream, result.Detail, MaxDetailBytes);
        WriteUInt16(stream, checked((ushort)result.Evidence.Count));
        WriteReservedUInt16(stream);
        foreach (var value in result.Evidence)
        {
            WriteScriptValue(stream, value);
        }

        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static ProductResult DecodeProductResult(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        var (agentStatus, schema) = reader.ReadResponsePrefix();
        if (agentStatus != AgentStatusOk)
        {
            reader.RequireEnd();
            return EmptyProductResult(agentStatus);
        }

        var availability = ReadEnum<ResultAvailability>(ref reader);
        var admission = ReadEnum<AdmissionState>(ref reader);
        var execution = ReadEnum<ExecutionState>(ref reader);
        var effect = ReadEnum<EffectState>(ref reader);
        var compensation = ReadEnum<CompensationState>(ref reader);
        reader.RequireReservedByte();
        var errorCode = ReadErrorCode(ref reader);
        var intentId = new IntentId(reader.ReadUInt64());
        var mapEpoch = new MapEpoch(reader.ReadUInt64());
        var productId = reader.ReadOptionalProductId();
        var detail = reader.ReadString(MaxDetailBytes);
        var evidenceCount = reader.ReadUInt16();
        if (evidenceCount > MaxEvidenceValues)
        {
            throw new InvalidDataException(
                $"Product result evidence count {evidenceCount} exceeds {MaxEvidenceValues}.");
        }

        reader.RequireReservedUInt16();
        var evidence = new ScriptValue[evidenceCount];
        for (var index = 0; index < evidenceCount; index++)
        {
            evidence[index] = reader.ReadScriptValue();
        }

        reader.RequireEnd();
        return new ProductResult(
            agentStatus,
            availability,
            admission,
            execution,
            effect,
            compensation,
            errorCode,
            intentId,
            mapEpoch,
            productId,
            detail,
            evidence);
    }

    // ---------- Command 60: Get Desired Intents ----------

    public static byte[] EncodeGetDesiredRequest(GetDesiredRequest request)
    {
        if (request.Limit is 0 or > MaxGetDesiredLimit)
        {
            throw new InvalidDataException(
                $"Get desired limit {request.Limit} must be between 1 and {MaxGetDesiredLimit}.");
        }

        using var stream = new MemoryStream();
        WriteUInt16(stream, SchemaVersion);
        WriteUInt32(stream, request.Offset);
        WriteUInt32(stream, request.Limit);
        WriteUInt64(stream, request.KnownPolicyRevision.Value);
        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static GetDesiredRequest DecodeGetDesiredRequest(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        reader.EnsureSchemaVersion();
        var offset = reader.ReadUInt32();
        var limit = reader.ReadUInt32();
        if (limit is 0 or > MaxGetDesiredLimit)
        {
            throw new InvalidDataException(
                $"Get desired limit {limit} must be between 1 and {MaxGetDesiredLimit}.");
        }

        var knownRevision = new PolicyRevision(reader.ReadUInt64());
        reader.RequireEnd();
        return new GetDesiredRequest(offset, limit, knownRevision);
    }

    public static byte[] EncodeGetDesiredResponse(GetDesiredResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Items.Count > MaxDesiredRegistry)
        {
            throw new InvalidDataException(
                $"Get desired response item count {response.Items.Count} exceeds {MaxDesiredRegistry}.");
        }

        using var stream = new MemoryStream();
        WriteResponsePrefix(stream, response.AgentStatusCode);
        if (response.AgentStatusCode != AgentStatusOk)
        {
            EnsurePayloadLimit(stream.Length);
            return stream.ToArray();
        }

        WriteUInt64(stream, response.PolicyRevision.Value);
        WriteUInt32(stream, response.TotalCount);
        WriteUInt32(stream, checked((uint)response.Items.Count));
        foreach (var item in response.Items)
        {
            ValidateDesiredIntentSummary(item);
            WriteUInt64(stream, item.IntentId.Value);
            WriteProductId(stream, item.ProductId);
            stream.WriteByte((byte)item.BindingKind);
            stream.WriteByte((byte)item.ScopeKind);
            stream.WriteByte((byte)item.ReapplyPolicy);
            stream.WriteByte((byte)item.DesiredState);
            WriteUInt64(stream, item.LastMapEpoch.Value);
        }

        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static GetDesiredResponse DecodeGetDesiredResponse(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        var (agentStatus, schema) = reader.ReadResponsePrefix();
        if (agentStatus != AgentStatusOk)
        {
            reader.RequireEnd();
            return new GetDesiredResponse(agentStatus, default, 0, Array.Empty<DesiredIntentSummary>());
        }

        var policyRevision = new PolicyRevision(reader.ReadUInt64());
        var totalCount = reader.ReadUInt32();
        var itemCount = reader.ReadUInt32();
        if (itemCount > MaxDesiredRegistry)
        {
            throw new InvalidDataException(
                $"Get desired response item count {itemCount} exceeds {MaxDesiredRegistry}.");
        }

        var items = new DesiredIntentSummary[itemCount];
        for (var index = 0; index < itemCount; index++)
        {
            var intentId = new IntentId(reader.ReadUInt64());
            var productId = reader.ReadProductId();
            var binding = ReadEnum<BindingKind>(ref reader);
            var scope = ReadEnum<ScopeKind>(ref reader);
            var reapply = ReadEnum<ReapplyPolicy>(ref reader);
            var desired = ReadEnum<DesiredState>(ref reader);
            var lastMapEpoch = new MapEpoch(reader.ReadUInt64());
            items[index] = new DesiredIntentSummary(
                intentId, productId, binding, scope, reapply, desired, lastMapEpoch);
        }

        reader.RequireEnd();
        return new GetDesiredResponse(agentStatus, policyRevision, totalCount, items);
    }

    // ---------- Command 61: Apply Durable Product Policy ----------

    public static byte[] EncodeApplyPolicyRequest(ApplyPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Entries);
        if (request.Entries.Count > MaxPolicyEntries)
        {
            throw new InvalidDataException(
                $"Durable policy entry count {request.Entries.Count} exceeds {MaxPolicyEntries}.");
        }

        foreach (var entry in request.Entries)
        {
            ValidatePolicyEntry(entry);
        }

        using var stream = new MemoryStream();
        WriteUInt16(stream, SchemaVersion);
        WriteUInt64(stream, request.PolicyRevision.Value);
        WriteUInt32(stream, checked((uint)request.Entries.Count));
        foreach (var entry in request.Entries)
        {
            WriteUInt64(stream, entry.PolicyEntryId);
            WriteProductId(stream, entry.ProductId);
            stream.WriteByte((byte)entry.ScopeKind);
            stream.WriteByte((byte)entry.ReapplyPolicy);
            WriteUInt16(stream, checked((ushort)entry.Parameters.Count));
            WriteReservedUInt16(stream);
            foreach (var parameter in entry.Parameters)
            {
                WriteScriptValue(stream, parameter);
            }
        }

        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static ApplyPolicyRequest DecodeApplyPolicyRequest(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        reader.EnsureSchemaVersion();
        var policyRevision = new PolicyRevision(reader.ReadUInt64());
        var entryCount = reader.ReadUInt32();
        if (entryCount > MaxPolicyEntries)
        {
            throw new InvalidDataException(
                $"Durable policy entry count {entryCount} exceeds {MaxPolicyEntries}.");
        }

        var entries = new DurablePolicyEntry[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            var policyEntryId = reader.ReadUInt64();
            var productId = reader.ReadProductId();
            var scope = ReadEnum<ScopeKind>(ref reader);
            var reapply = ReadEnum<ReapplyPolicy>(ref reader);
            var parameterCount = reader.ReadUInt16();
            if (parameterCount > MaxIntentParameters)
            {
                throw new InvalidDataException(
                    $"Policy entry parameter count {parameterCount} exceeds {MaxIntentParameters}.");
            }

            reader.RequireReservedUInt16();
            var parameters = new ScriptValue[parameterCount];
            for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                parameters[parameterIndex] = reader.ReadScriptValue();
            }

            entries[index] = new DurablePolicyEntry(policyEntryId, productId, scope, reapply, parameters);
            ValidatePolicyEntry(entries[index]);
        }

        reader.RequireEnd();
        return new ApplyPolicyRequest(policyRevision, entries);
    }

    public static byte[] EncodeApplyPolicyResponse(ApplyPolicyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(response.Results);
        if (response.Results.Count > MaxPolicyEntries)
        {
            throw new InvalidDataException(
                $"Policy import result count {response.Results.Count} exceeds {MaxPolicyEntries}.");
        }

        foreach (var result in response.Results)
        {
            if (!Enum.IsDefined(result.ErrorCode))
            {
                throw new InvalidDataException("Policy import result contains an unknown error code.");
            }
        }

        using var stream = new MemoryStream();
        WriteResponsePrefix(stream, response.AgentStatusCode);
        if (response.AgentStatusCode != AgentStatusOk)
        {
            EnsurePayloadLimit(stream.Length);
            return stream.ToArray();
        }

        WriteUInt64(stream, response.PolicyRevision.Value);
        WriteUInt32(stream, response.AcceptedCount);
        WriteUInt32(stream, response.RejectedCount);
        WriteUInt32(stream, checked((uint)response.Results.Count));
        foreach (var result in response.Results)
        {
            WriteUInt64(stream, result.PolicyEntryId);
            WriteUInt64(stream, result.IntentId.Value);
            WriteUInt16(stream, (ushort)result.ErrorCode);
            WriteReservedUInt16(stream);
        }

        EnsurePayloadLimit(stream.Length);
        return stream.ToArray();
    }

    public static ApplyPolicyResponse DecodeApplyPolicyResponse(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLimit(payload.Length);
        var reader = new Reader(payload);
        var (agentStatus, schema) = reader.ReadResponsePrefix();
        if (agentStatus != AgentStatusOk)
        {
            reader.RequireEnd();
            return new ApplyPolicyResponse(agentStatus, default, 0, 0, Array.Empty<DurablePolicyImportResult>());
        }

        var policyRevision = new PolicyRevision(reader.ReadUInt64());
        var acceptedCount = reader.ReadUInt32();
        var rejectedCount = reader.ReadUInt32();
        var resultCount = reader.ReadUInt32();
        if (resultCount > MaxPolicyEntries)
        {
            throw new InvalidDataException(
                $"Policy import result count {resultCount} exceeds {MaxPolicyEntries}.");
        }

        var results = new DurablePolicyImportResult[resultCount];
        for (var index = 0; index < resultCount; index++)
        {
            var policyEntryId = reader.ReadUInt64();
            var intentId = new IntentId(reader.ReadUInt64());
            var errorCode = ReadErrorCode(ref reader);
            reader.RequireReservedUInt16();
            results[index] = new DurablePolicyImportResult(policyEntryId, intentId, errorCode);
        }

        reader.RequireEnd();
        return new ApplyPolicyResponse(agentStatus, policyRevision, acceptedCount, rejectedCount, results);
    }

    // ---------- Validation helpers ----------

    private static void ValidateScopeMask(ScopeMask mask)
    {
        if (((byte)mask & ~ScopeMaskMask) != 0)
        {
            throw new InvalidDataException($"Scope mask {(byte)mask} contains undefined bits.");
        }
    }

    private static ScopeMask ReadScopeMask(ref Reader reader)
    {
        var raw = reader.ReadByte();
        if ((raw & ~ScopeMaskMask) != 0)
        {
            throw new InvalidDataException($"Scope mask {raw} contains undefined bits.");
        }

        return (ScopeMask)raw;
    }

    private static RuntimeFlags ReadRuntimeFlags(ref Reader reader)
    {
        var raw = reader.ReadByte();
        if ((raw & ~RuntimeFlagsMask) != 0)
        {
            throw new InvalidDataException($"Runtime flags {raw} contain undefined bits.");
        }

        return (RuntimeFlags)raw;
    }

    private static T ReadEnum<T>(ref Reader reader)
        where T : struct, Enum
    {
        var raw = reader.ReadByte();
        var value = (T)Enum.ToObject(typeof(T), raw);
        if (!Enum.IsDefined(value))
        {
            throw new InvalidDataException($"Unknown {typeof(T).Name} tag {raw}.");
        }

        return value;
    }

    private static ProductErrorCode ReadErrorCode(ref Reader reader)
    {
        var raw = reader.ReadUInt16();
        if (!Enum.IsDefined((ProductErrorCode)raw))
        {
            throw new InvalidDataException($"Unknown product error code {raw}.");
        }

        return (ProductErrorCode)raw;
    }

    private static void ValidateQueryContextResponse(QueryContextResponse response)
    {
        if (!Enum.IsDefined(response.Lifecycle) ||
            !Enum.IsDefined(response.SinglePlayerProof))
        {
            throw new InvalidDataException("Query context response contains an unknown tag.");
        }

        if (((byte)response.RuntimeFlags & ~RuntimeFlagsMask) != 0)
        {
            throw new InvalidDataException("Query context response runtime flags contain undefined bits.");
        }

        ValidateScopeMask(response.ScopeAvailabilityMask);
        ValidateToken(response.CurrentPlayerToken, optional: true);
    }

    private static void ValidateContextBinding(ContextBinding binding, bool allowCaptured)
    {
        if (!Enum.IsDefined(binding.Kind) ||
            !Enum.IsDefined(binding.Scope) ||
            !Enum.IsDefined(binding.Reapply))
        {
            throw new InvalidDataException("Context binding contains an unknown tag.");
        }

        var scopeAllowed = binding.Kind switch
        {
            BindingKind.Live => binding.Scope == ScopeKind.CurrentPlayer,
            BindingKind.Rebindable => binding.Scope is ScopeKind.CurrentPlayer
                or ScopeKind.AllOtherPlayers
                or ScopeKind.AllUnits,
            BindingKind.Captured => binding.Scope is ScopeKind.SelectedUnit
                or ScopeKind.SelectedObject
                or ScopeKind.FixedPlayer,
            _ => false,
        };

        if (!scopeAllowed)
        {
            throw new InvalidDataException(
                $"Scope {binding.Scope} is not valid for binding kind {binding.Kind}.");
        }

        if (binding.Kind == BindingKind.Live && binding.Reapply != ReapplyPolicy.None)
        {
            throw new InvalidDataException("Live binding must use ReapplyPolicy.None.");
        }

        if (binding.Kind == BindingKind.Captured)
        {
            if (binding.Reapply != ReapplyPolicy.None)
            {
                throw new InvalidDataException("Captured binding must use ReapplyPolicy.None.");
            }

            if (binding.Captured is null)
            {
                throw new InvalidDataException("Captured binding is missing its captured target payload.");
            }

            ValidateCapturedObjectIds(binding.Captured.ObjectIds);
        }
        else if (binding.Captured is not null)
        {
            throw new InvalidDataException("Non-captured binding must not carry a captured target.");
        }
    }

    private static void ValidatePolicyEntry(DurablePolicyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Enum.IsDefined(entry.ScopeKind) || !Enum.IsDefined(entry.ReapplyPolicy))
        {
            throw new InvalidDataException("Policy entry contains an unknown tag.");
        }

        if (entry.PolicyEntryId == 0)
        {
            throw new InvalidDataException("Policy entry id must be non-zero.");
        }

        if (entry.ScopeKind is not (ScopeKind.CurrentPlayer or ScopeKind.AllOtherPlayers or ScopeKind.AllUnits))
        {
            throw new InvalidDataException(
                $"Policy entry scope {entry.ScopeKind} is not a symbolic scope.");
        }

        if (entry.ReapplyPolicy is not (ReapplyPolicy.None or ReapplyPolicy.OnReadyOnce))
        {
            throw new InvalidDataException(
                $"Policy entry reapply policy {entry.ReapplyPolicy} is not allowed.");
        }

        ValidateParameterCount(entry.Parameters.Count);
    }

    private static void ValidateDesiredIntentSummary(DesiredIntentSummary item)
    {
        if (!Enum.IsDefined(item.BindingKind) ||
            !Enum.IsDefined(item.ScopeKind) ||
            !Enum.IsDefined(item.ReapplyPolicy) ||
            !Enum.IsDefined(item.DesiredState))
        {
            throw new InvalidDataException("Desired intent summary contains an unknown tag.");
        }
    }

    private static void ValidateParameterCount(int count)
    {
        if (count < 0 || count > MaxIntentParameters)
        {
            throw new InvalidDataException(
                $"Parameter count {count} exceeds {MaxIntentParameters}.");
        }
    }

    private static void ValidateProductResult(ProductResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Availability) ||
            !Enum.IsDefined(result.Admission) ||
            !Enum.IsDefined(result.Execution) ||
            !Enum.IsDefined(result.Effect) ||
            !Enum.IsDefined(result.Compensation) ||
            !Enum.IsDefined(result.ErrorCode))
        {
            throw new InvalidDataException("Product result contains an unknown tag.");
        }

        if (result.Evidence.Count > MaxEvidenceValues)
        {
            throw new InvalidDataException(
                $"Product result evidence count {result.Evidence.Count} exceeds {MaxEvidenceValues}.");
        }

        if (result.Detail is null)
        {
            throw new InvalidDataException("Product result detail is null.");
        }
    }

    private static void ValidateToken(AgentOwnedToken? token, bool optional)
    {
        if (token is null)
        {
            if (!optional)
            {
                throw new InvalidDataException("Captured binding is missing its token.");
            }
            return;
        }

        var length = token.Length;
        if (length is 0)
        {
            if (!optional || token.TokenKind != 0)
            {
                throw new InvalidDataException("Captured token must be 1..64 bytes.");
            }

            return;
        }

        if (token.TokenKind == 0)
        {
            throw new InvalidDataException("Context token kind must be non-zero when token bytes are present.");
        }

        if (length > MaxContextTokenBytes)
        {
            throw new InvalidDataException(
                $"Context token length {length} exceeds {MaxContextTokenBytes} bytes.");
        }
    }

    private static void ValidateSubmitIntentResponse(SubmitIntentResponse response)
    {
        if (!Enum.IsDefined(response.Acceptance) || !Enum.IsDefined(response.ErrorCode))
        {
            throw new InvalidDataException("Submit intent response contains an unknown tag.");
        }
        if (response.AgentStatusCode != AgentStatusOk)
        {
            return;
        }

        var valid = response.Acceptance switch
        {
            ProductAcceptance.Accepted =>
                response.IntentId.Value != 0 && response.ErrorCode == ProductErrorCode.None,
            ProductAcceptance.Rejected =>
                response.IntentId.Value == 0 && response.ErrorCode != ProductErrorCode.None,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidDataException(
                "Submit intent response acceptance, error code and IntentId are inconsistent.");
        }
    }

    private static QueryContextResponse EmptyQueryContextResponse(ushort agentStatus) =>
        new(
            agentStatus,
            MatchLifecycle.Unavailable,
            SinglePlayerProof.Unknown,
            RuntimeFlags.None,
            ScopeMask.None,
            0,
            default,
            default,
            default,
            null);

    private static ProductResult EmptyProductResult(ushort agentStatus) =>
        new(
            agentStatus,
            ResultAvailability.UnknownIntent,
            AdmissionState.Pending,
            ExecutionState.NotStarted,
            EffectState.NotApplicable,
            CompensationState.NotRequired,
            ProductErrorCode.None,
            default,
            default,
            null,
            string.Empty,
            Array.Empty<ScriptValue>());

    // ---------- Write helpers ----------

    private static void WriteResponsePrefix(Stream stream, ushort agentStatusCode)
    {
        WriteUInt16(stream, agentStatusCode);
        WriteUInt16(stream, SchemaVersion);
    }

    private static void WriteBindingHeader(Stream stream, ContextBinding binding)
    {
        stream.WriteByte((byte)binding.Kind);
        stream.WriteByte((byte)binding.Scope);
        stream.WriteByte((byte)binding.Reapply);
        WriteReservedByte(stream);
    }

    // Reads the 4-byte binding header (kind/scope/reapply/reserved). The optional captured
    // payload is read later by the caller, after parameterCount/reserved, so the section 7
    // wire order is preserved.
    private static ContextBinding ReadBinding(ref Reader reader)
    {
        var kind = ReadEnum<BindingKind>(ref reader);
        var scope = ReadEnum<ScopeKind>(ref reader);
        var reapply = ReadEnum<ReapplyPolicy>(ref reader);
        reader.RequireReservedByte();
        return new ContextBinding(kind, scope, reapply);
    }

    private static void WriteCapturedBlock(Stream stream, CapturedTarget captured)
    {
        WriteUInt16(stream, checked((ushort)captured.ObjectIds.Length));
        WriteReservedUInt16(stream);
        foreach (var objectId in captured.ObjectIds)
        {
            WriteUInt32(stream, objectId);
        }
    }

    private static CapturedTarget ReadCapturedBlock(ref Reader reader)
    {
        var objectCount = reader.ReadUInt16();
        reader.RequireReservedUInt16();
        if (objectCount is 0 or > MaxCapturedObjectIds)
        {
            throw new InvalidDataException(
                $"Captured object count {objectCount} must be between 1 and {MaxCapturedObjectIds}.");
        }

        var objectIds = ImmutableArray.CreateBuilder<uint>(objectCount);
        for (var index = 0; index < objectCount; ++index)
        {
            objectIds.Add(reader.ReadUInt32());
        }

        var captured = new CapturedTarget(objectIds.MoveToImmutable());
        ValidateCapturedObjectIds(captured.ObjectIds);
        return captured;
    }

    private static void ValidateCapturedObjectIds(ImmutableArray<uint> objectIds)
    {
        if (objectIds.IsDefaultOrEmpty || objectIds.Length > MaxCapturedObjectIds)
        {
            throw new InvalidDataException(
                $"Captured object count must be between 1 and {MaxCapturedObjectIds}.");
        }

        var unique = new HashSet<uint>();
        foreach (var objectId in objectIds)
        {
            if (objectId == 0 || !unique.Add(objectId))
            {
                throw new InvalidDataException(
                    "Captured object IDs must be non-zero and unique.");
            }
        }
    }

    private static void WriteToken(Stream stream, AgentOwnedToken token)
    {
        stream.WriteByte(token.TokenKind);
        WriteReservedByte(stream);
        WriteUInt16(stream, checked((ushort)token.Length));
        stream.Write(token.Token.AsSpan());
    }

    private static void WriteOptionalToken(Stream stream, AgentOwnedToken? token)
    {
        if (token is null || token.Length == 0)
        {
            stream.WriteByte(0);
            WriteReservedByte(stream);
            WriteUInt16(stream, 0);
            return;
        }

        WriteToken(stream, token);
    }

    private static AgentOwnedToken? ReadOptionalToken(ref Reader reader)
    {
        var tokenKind = reader.ReadByte();
        reader.RequireReservedByte();
        var length = reader.ReadUInt16();
        var bytes = reader.ReadBytes(length);
        if (tokenKind == 0 || length == 0)
        {
            if (tokenKind != 0 || length != 0)
            {
                throw new InvalidDataException("Optional token with kind 0 must have zero length.");
            }

            return null;
        }

        if (length is < 1 or > MaxContextTokenBytes)
        {
            throw new InvalidDataException(
                $"Context token length {length} must be between 1 and {MaxContextTokenBytes}.");
        }

        return new AgentOwnedToken(tokenKind, bytes.ToImmutableArray());
    }

    private static void WriteProductId(Stream stream, ProductId productId)
    {
        var bytes = Utf8.GetBytes(productId.Value);
        if (bytes.Length > MaxProductIdBytes)
        {
            throw new InvalidDataException(
                $"ProductId exceeds {MaxProductIdBytes} UTF-8 bytes.");
        }

        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteOptionalProductId(Stream stream, ProductId? productId)
    {
        if (productId is null)
        {
            WriteUInt32(stream, 0);
            return;
        }

        WriteProductId(stream, productId.Value);
    }

    private static void WriteBoundedString(Stream stream, string value, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Utf8.GetBytes(value);
        if (bytes.Length > maxBytes)
        {
            throw new InvalidDataException($"String payload exceeds {maxBytes} bytes.");
        }

        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteReservedByte(Stream stream) => stream.WriteByte(0);

    private static void WriteReservedUInt16(Stream stream) => WriteUInt16(stream, 0);

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    // ScriptValue uses the frozen product-control value encoding:
    // u8 kind, u32 bodyLength, body. Integer/Real are little-endian 8 bytes; boolean is 0/1;
    // string-like values embed a u32 byte length + strict UTF-8 bytes as their body.
    private static void WriteScriptValue(Stream stream, ScriptValue value)
    {
        if (!Enum.IsDefined(value.Kind))
        {
            throw new InvalidDataException($"Unknown ScriptValue kind {(byte)value.Kind}.");
        }

        stream.WriteByte((byte)value.Kind);
        using var body = new MemoryStream();
        switch (value.Kind)
        {
            case ScriptValueKind.Null:
                break;
            case ScriptValueKind.Integer:
                WriteInt64(body, value.IntegerValue);
                break;
            case ScriptValueKind.Real:
                WriteDouble(body, value.RealValue);
                break;
            case ScriptValueKind.Boolean:
                body.WriteByte(value.BooleanValue ? (byte)1 : (byte)0);
                break;
            case ScriptValueKind.String:
            case ScriptValueKind.PlayerRef:
            case ScriptValueKind.ObjectRef:
            case ScriptValueKind.Unavailable:
                if (value.TextValue is null)
                {
                    throw new InvalidDataException($"{value.Kind} requires a text payload.");
                }

                WriteBoundedString(body, value.TextValue, MaxGenericStringBytes);
                break;
            default:
                throw new InvalidDataException($"Unknown ScriptValue kind {(byte)value.Kind}.");
        }

        WriteUInt32(stream, checked((uint)body.Length));
        body.Position = 0;
        body.CopyTo(stream);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, BitConverter.DoubleToInt64Bits(value));
        stream.Write(buffer);
    }

    private static void EnsurePayloadLimit(long length)
    {
        if (length > AgentProtocol.MaxPayloadLength)
        {
            throw new InvalidDataException(
                $"Product control payload exceeds {AgentProtocol.MaxPayloadLength} bytes.");
        }
    }

    // ---------- Reader ----------

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> m_payload;
        private int m_offset;

        public Reader(ReadOnlySpan<byte> payload)
        {
            m_payload = payload;
            m_offset = 0;
        }

        public byte ReadByte()
        {
            EnsureAvailable(sizeof(byte));
            return m_payload[m_offset++];
        }

        public ushort ReadUInt16()
        {
            EnsureAvailable(sizeof(ushort));
            var value = BinaryPrimitives.ReadUInt16LittleEndian(m_payload.Slice(m_offset, sizeof(ushort)));
            m_offset += sizeof(ushort);
            return value;
        }

        public uint ReadUInt32()
        {
            EnsureAvailable(sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(m_payload.Slice(m_offset, sizeof(uint)));
            m_offset += sizeof(uint);
            return value;
        }

        public ulong ReadUInt64()
        {
            EnsureAvailable(sizeof(ulong));
            var value = BinaryPrimitives.ReadUInt64LittleEndian(m_payload.Slice(m_offset, sizeof(ulong)));
            m_offset += sizeof(ulong);
            return value;
        }

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            EnsureAvailable(length);
            var bytes = m_payload.Slice(m_offset, length);
            m_offset += length;
            return bytes;
        }

        public string ReadString(int maxBytes)
        {
            var length = ReadUInt32();
            if (length > (uint)maxBytes)
            {
                throw new InvalidDataException($"String payload exceeds {maxBytes} bytes.");
            }

            var bytes = ReadBytes(checked((int)length));
            try
            {
                return Utf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("String payload is not valid strict UTF-8.", ex);
            }
        }

        public ProductId ReadProductId()
        {
            var text = ReadProductIdText(out var length);
            try
            {
                return new ProductId(text);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }
        }

        public ProductId? ReadOptionalProductId()
        {
            var text = ReadProductIdText(out var length);
            if (length == 0)
            {
                return null;
            }

            try
            {
                return new ProductId(text);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }
        }

        public AgentOwnedToken ReadRequiredToken()
        {
            var tokenKind = ReadByte();
            RequireReservedByte();
            var length = ReadUInt16();
            var bytes = ReadBytes(length).ToArray();
            if (tokenKind == 0)
            {
                throw new InvalidDataException("Captured binding token kind must be non-zero.");
            }

            if (length is < 1 or > MaxContextTokenBytes)
            {
                throw new InvalidDataException(
                    $"Context token length {length} must be between 1 and {MaxContextTokenBytes}.");
            }

            return new AgentOwnedToken(tokenKind, bytes.ToImmutableArray());
        }

        public ScriptValue ReadScriptValue()
        {
            var kind = (ScriptValueKind)ReadByte();
            var length = ReadUInt32();
            var payload = ReadBytes(checked((int)length));
            return kind switch
            {
                ScriptValueKind.Null when payload.Length == 0 => ScriptValue.Null(),
                ScriptValueKind.Integer when payload.Length == sizeof(long) =>
                    ScriptValue.Integer(BinaryPrimitives.ReadInt64LittleEndian(payload)),
                ScriptValueKind.Real when payload.Length == sizeof(double) =>
                    ScriptValue.Real(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(payload))),
                ScriptValueKind.Boolean when payload.Length == sizeof(byte) && payload[0] is 0 or 1 =>
                    ScriptValue.Boolean(payload[0] == 1),
                ScriptValueKind.String or ScriptValueKind.PlayerRef or ScriptValueKind.ObjectRef or ScriptValueKind.Unavailable =>
                    ReadTextScriptValue(kind, payload),
                _ => throw new InvalidDataException(
                    $"Invalid ScriptValue kind or payload {(byte)kind}/{payload.Length}."),
            };
        }

        public void EnsureSchemaVersion()
        {
            var version = ReadUInt16();
            if (version != SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported Product control schema version {version}; expected {SchemaVersion}.");
            }
        }

        public (ushort AgentStatus, ushort Schema) ReadResponsePrefix()
        {
            var agentStatus = ReadUInt16();
            var schema = ReadUInt16();
            if (schema != SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported Product control schema version {schema}; expected {SchemaVersion}.");
            }

            return (agentStatus, schema);
        }

        public void RequireReservedByte()
        {
            if (ReadByte() != 0)
            {
                throw new InvalidDataException("Reserved byte must be zero.");
            }
        }

        public void RequireReservedUInt16()
        {
            if (ReadUInt16() != 0)
            {
                throw new InvalidDataException("Reserved uint16 must be zero.");
            }
        }

        public void RequireEnd()
        {
            if (m_offset != m_payload.Length)
            {
                throw new InvalidDataException("Trailing bytes found after Product control payload.");
            }
        }

        private string ReadProductIdText(out int length)
        {
            var rawLength = ReadUInt32();
            if (rawLength > (uint)MaxProductIdBytes)
            {
                throw new InvalidDataException(
                    $"ProductId payload exceeds {MaxProductIdBytes} bytes.");
            }

            length = checked((int)rawLength);
            var bytes = ReadBytes(length);
            try
            {
                return Utf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("ProductId payload is not valid strict UTF-8.", ex);
            }
        }

        private void EnsureAvailable(int length)
        {
            if (length < 0 || length > m_payload.Length - m_offset)
            {
                throw new InvalidDataException("Truncated Product control payload.");
            }
        }

        private static ScriptValue ReadTextScriptValue(ScriptValueKind kind, ReadOnlySpan<byte> payload)
        {
            var valueReader = new Reader(payload);
            var text = valueReader.ReadString(MaxGenericStringBytes);
            valueReader.RequireEnd();
            return kind switch
            {
                ScriptValueKind.String => ScriptValue.String(text),
                ScriptValueKind.PlayerRef => ScriptValue.PlayerRef(text),
                ScriptValueKind.ObjectRef => ScriptValue.ObjectRef(text),
                ScriptValueKind.Unavailable => ScriptValue.Unavailable(text),
                _ => throw new InvalidDataException($"Unsupported text ScriptValue kind {(byte)kind}."),
            };
        }
    }
}
