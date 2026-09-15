using System.Buffers.Binary;
using System.Text;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Wire codec for the Reinforcement Preset Console v1 (Agent pipe commands 62/63).
/// Native mirror: src/RayaTrainer.Agent/ReinforcementConsole/ReinforcementPresetWireCodec.{h,cpp}.
/// Both sides MUST produce and accept byte-for-byte identical payloads.
///
/// All integers are little-endian. Strings are length-prefixed (u32 byteCount) strict UTF-8
/// without NUL bytes. Decoding is strictly fail-closed: truncation, trailing bytes, unknown
/// enum values, non-zero reserved bytes, over-limit counts/strings and duplicate preset names
/// all throw <see cref="InvalidDataException"/>. Responses with a non-Ok AgentStatusCode are
/// encoded/decoded as the 4-byte prefix only (status + schemaVersion), mirroring the Product
/// Control convention.
/// </summary>
public static class ReinforcementPresetConsoleWireCodec
{
    public const ushort SchemaVersion = ReinforcementPresetConsoleLimits.SchemaVersion;
    public const ushort AgentStatusOk = 0;

    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    // ---------- Command 62: Replace Reinforcement Preset Projection ----------

    public static byte[] EncodeReplaceProjectionRequest(ReinforcementPresetProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ValidateProjection(projection);

        using var stream = new MemoryStream();
        WriteUInt16(stream, SchemaVersion);
        stream.WriteByte((byte)projection.Validity);
        stream.WriteByte(0); // reserved
        WriteUInt64(stream, projection.ProjectionSessionId);
        WriteUInt64(stream, projection.Generation);
        WriteOptionalString(stream, projection.PreferredSelectedName, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
        WriteOptionalString(stream, projection.SyncError, ReinforcementPresetConsoleLimits.MaxSyncErrorBytes);
        WriteUInt32(stream, (uint)projection.Presets.Count);
        foreach (var preset in projection.Presets)
        {
            WriteString(stream, preset.Name, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
            WriteUInt32(stream, (uint)preset.Entries.Count);
            foreach (var entry in preset.Entries)
            {
                WriteString(stream, entry.DisplayName, ReinforcementPresetConsoleLimits.MaxDisplayNameBytes);
                WriteUInt32(stream, entry.UnitId);
                WriteUInt32(stream, (uint)entry.Count);
                WriteUInt32(stream, (uint)entry.Rank);
            }
        }

        var payload = stream.ToArray();
        if (payload.Length > AgentProtocol.MaxPayloadLength)
        {
            throw new InvalidDataException($"Reinforcement projection payload {payload.Length} exceeds the pipe limit.");
        }
        return payload;
    }

    public static ReinforcementPresetProjection DecodeReplaceProjectionRequest(ReadOnlySpan<byte> payload)
    {
        var reader = new Reader(payload);
        var schema = reader.ReadUInt16();
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException($"Reinforcement projection schema {schema} is not supported.");
        }
        var validityRaw = reader.ReadByte();
        if (validityRaw is not ((byte)ReinforcementProjectionValidity.Valid) and not ((byte)ReinforcementProjectionValidity.Invalid))
        {
            throw new InvalidDataException($"Unknown projection validity {validityRaw}.");
        }
        var validity = (ReinforcementProjectionValidity)validityRaw;
        if (reader.ReadByte() != 0)
        {
            throw new InvalidDataException("Reserved byte must be zero.");
        }
        var sessionId = reader.ReadUInt64();
        if (sessionId == 0 || sessionId > 0x7FFF_FFFF_FFFF_FFFFUL)
        {
            throw new InvalidDataException("Projection session id must be a nonzero positive 63-bit value.");
        }
        var generation = reader.ReadUInt64();
        if (generation == 0)
        {
            throw new InvalidDataException("Projection generation must start at 1.");
        }
        var preferred = ReadOptionalString(ref reader, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
        var syncError = ReadOptionalString(ref reader, ReinforcementPresetConsoleLimits.MaxSyncErrorBytes);
        var presetCount = reader.ReadUInt32();
        if (validity == ReinforcementProjectionValidity.Invalid && presetCount != 0)
        {
            throw new InvalidDataException("Invalid projection must not carry presets.");
        }
        if (presetCount > ReinforcementPresetConsoleLimits.MaxPresets)
        {
            throw new InvalidDataException($"Preset count {presetCount} exceeds limit {ReinforcementPresetConsoleLimits.MaxPresets}.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var presets = new List<ReinforcementProjectionPreset>((int)presetCount);
        for (var i = 0; i < presetCount; i++)
        {
            var name = ReadString(ref reader, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
            if (!names.Add(name))
            {
                throw new InvalidDataException($"Duplicate preset name '{name}'.");
            }
            var entryCount = reader.ReadUInt32();
            if (entryCount is 0 or > ReinforcementPresetConsoleLimits.MaxEntriesPerPreset)
            {
                throw new InvalidDataException($"Preset '{name}' entry count {entryCount} is out of range.");
            }
            var entries = new List<ReinforcementProjectionEntry>((int)entryCount);
            for (var j = 0; j < entryCount; j++)
            {
                var displayName = ReadString(ref reader, ReinforcementPresetConsoleLimits.MaxDisplayNameBytes);
                var unitId = reader.ReadUInt32();
                var count = reader.ReadUInt32();
                var rank = reader.ReadUInt32();
                if (unitId == 0 || count is < 1 or > 50 || rank > 3)
                {
                    throw new InvalidDataException($"Preset '{name}' entry '{displayName}' carries illegal values.");
                }
                entries.Add(new ReinforcementProjectionEntry(displayName, unitId, (int)count, (int)rank));
            }
            presets.Add(new ReinforcementProjectionPreset(name, entries));
        }

        reader.RequireEnd();
        if (preferred is not null && !names.Contains(preferred))
        {
            // A preferred selection that no longer matches any preset decodes as no selection.
            preferred = null;
        }
        return new ReinforcementPresetProjection(validity, sessionId, generation, preferred, syncError, presets);
    }

    public static byte[] EncodeReplaceProjectionResponse(ReplaceReinforcementProjectionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        using var stream = new MemoryStream();
        WriteUInt16(stream, response.AgentStatusCode);
        WriteUInt16(stream, SchemaVersion);
        if (response.AgentStatusCode != AgentStatusOk)
        {
            return stream.ToArray();
        }

        stream.WriteByte(response.Accepted ? (byte)1 : (byte)0);
        stream.WriteByte(0); // reserved
        WriteUInt16(stream, (ushort)response.RejectReason);
        WriteUInt64(stream, response.AcceptedSessionId);
        WriteUInt64(stream, response.AcceptedGeneration);
        WriteOptionalString(stream, response.SelectedName, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
        return stream.ToArray();
    }

    public static ReplaceReinforcementProjectionResponse DecodeReplaceProjectionResponse(ReadOnlySpan<byte> payload)
    {
        var reader = new Reader(payload);
        var status = reader.ReadUInt16();
        var schema = reader.ReadUInt16();
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException($"Reinforcement projection response schema {schema} is not supported.");
        }
        if (status != AgentStatusOk)
        {
            reader.RequireEnd();
            return new ReplaceReinforcementProjectionResponse(status, false, ReinforcementProjectionRejectReason.None, 0, 0, null);
        }

        var acceptedRaw = reader.ReadByte();
        if (acceptedRaw > 1)
        {
            throw new InvalidDataException($"Unknown accepted flag {acceptedRaw}.");
        }
        if (reader.ReadByte() != 0)
        {
            throw new InvalidDataException("Reserved byte must be zero.");
        }
        var reasonRaw = reader.ReadUInt16();
        if (reasonRaw > (ushort)ReinforcementProjectionRejectReason.ReservedNonZero)
        {
            throw new InvalidDataException($"Unknown reject reason {reasonRaw}.");
        }
        var sessionId = reader.ReadUInt64();
        var generation = reader.ReadUInt64();
        var selected = ReadOptionalString(ref reader, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
        reader.RequireEnd();
        return new ReplaceReinforcementProjectionResponse(
            status,
            acceptedRaw == 1,
            (ReinforcementProjectionRejectReason)reasonRaw,
            sessionId,
            generation,
            selected);
    }

    // ---------- Command 63: Get Reinforcement Preset Console State ----------

    public static byte[] EncodeConsoleStateRequest()
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, SchemaVersion);
        WriteUInt16(stream, 0); // reserved
        return stream.ToArray();
    }

    public static void DecodeConsoleStateRequest(ReadOnlySpan<byte> payload)
    {
        var reader = new Reader(payload);
        var schema = reader.ReadUInt16();
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException($"Console state request schema {schema} is not supported.");
        }
        if (reader.ReadUInt16() != 0)
        {
            throw new InvalidDataException("Reserved field must be zero.");
        }
        reader.RequireEnd();
    }

    public static byte[] EncodeConsoleStateResponse(ReinforcementPresetConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var stream = new MemoryStream();
        WriteUInt16(stream, state.AgentStatusCode);
        WriteUInt16(stream, SchemaVersion);
        if (state.AgentStatusCode != AgentStatusOk)
        {
            return stream.ToArray();
        }

        stream.WriteByte(state.HasProjection ? (byte)1 : (byte)0);
        stream.WriteByte((byte)state.Validity);
        stream.WriteByte((byte)state.BatchState);
        stream.WriteByte(0); // reserved
        WriteUInt64(stream, state.ProjectionSessionId);
        WriteUInt64(stream, state.Generation);
        WriteUInt32(stream, state.PresetCount);
        WriteUInt32(stream, state.BatchTotal);
        WriteUInt32(stream, state.BatchCompleted);
        WriteUInt32(stream, state.BatchFailed);
        WriteUInt32(stream, state.BatchNotAttempted);
        WriteUInt64(stream, state.ActiveIntentId);
        WriteOptionalString(stream, state.SelectedName, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
        WriteOptionalString(stream, state.SyncError, ReinforcementPresetConsoleLimits.MaxSyncErrorBytes);
        return stream.ToArray();
    }

    public static ReinforcementPresetConsoleState DecodeConsoleStateResponse(ReadOnlySpan<byte> payload)
    {
        var reader = new Reader(payload);
        var status = reader.ReadUInt16();
        var schema = reader.ReadUInt16();
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException($"Console state response schema {schema} is not supported.");
        }
        if (status != AgentStatusOk)
        {
            reader.RequireEnd();
            return new ReinforcementPresetConsoleState(
                status, false, ReinforcementProjectionValidity.Invalid, 0, 0, 0, null, null,
                ReinforcementBatchWireState.None, 0, 0, 0, 0, 0);
        }

        var hasProjectionRaw = reader.ReadByte();
        if (hasProjectionRaw > 1)
        {
            throw new InvalidDataException($"Unknown projection flag {hasProjectionRaw}.");
        }
        var validityRaw = reader.ReadByte();
        if (validityRaw is not ((byte)ReinforcementProjectionValidity.Valid) and not ((byte)ReinforcementProjectionValidity.Invalid))
        {
            throw new InvalidDataException($"Unknown projection validity {validityRaw}.");
        }
        var batchStateRaw = reader.ReadByte();
        if (batchStateRaw > (byte)ReinforcementBatchWireState.Aborted)
        {
            throw new InvalidDataException($"Unknown batch state {batchStateRaw}.");
        }
        if (reader.ReadByte() != 0)
        {
            throw new InvalidDataException("Reserved byte must be zero.");
        }
        var sessionId = reader.ReadUInt64();
        var generation = reader.ReadUInt64();
        var presetCount = reader.ReadUInt32();
        var batchTotal = reader.ReadUInt32();
        var batchCompleted = reader.ReadUInt32();
        var batchFailed = reader.ReadUInt32();
        var batchNotAttempted = reader.ReadUInt32();
        var activeIntentId = reader.ReadUInt64();
        var selected = ReadOptionalString(ref reader, ReinforcementPresetConsoleLimits.MaxPresetNameBytes);
        var syncError = ReadOptionalString(ref reader, ReinforcementPresetConsoleLimits.MaxSyncErrorBytes);
        reader.RequireEnd();
        return new ReinforcementPresetConsoleState(
            status,
            hasProjectionRaw == 1,
            (ReinforcementProjectionValidity)validityRaw,
            sessionId,
            generation,
            presetCount,
            selected,
            syncError,
            (ReinforcementBatchWireState)batchStateRaw,
            batchTotal,
            batchCompleted,
            batchFailed,
            batchNotAttempted,
            activeIntentId);
    }

    // ---------- Validation ----------

    private static void ValidateProjection(ReinforcementPresetProjection projection)
    {
        if (projection.ProjectionSessionId == 0 || projection.ProjectionSessionId > 0x7FFF_FFFF_FFFF_FFFFUL)
        {
            throw new InvalidDataException("Projection session id must be a nonzero positive 63-bit value.");
        }
        if (projection.Generation == 0)
        {
            throw new InvalidDataException("Projection generation must start at 1.");
        }
        if (projection.Validity == ReinforcementProjectionValidity.Invalid)
        {
            if (projection.Presets.Count != 0)
            {
                throw new InvalidDataException("Invalid projection must not carry presets.");
            }
            return;
        }
        if (projection.Presets.Count > ReinforcementPresetConsoleLimits.MaxPresets)
        {
            throw new InvalidDataException($"Preset count {projection.Presets.Count} exceeds limit {ReinforcementPresetConsoleLimits.MaxPresets}.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in projection.Presets)
        {
            if (!names.Add(preset.Name))
            {
                throw new InvalidDataException($"Duplicate preset name '{preset.Name}'.");
            }
            if (preset.Entries.Count is 0 or > ReinforcementPresetConsoleLimits.MaxEntriesPerPreset)
            {
                throw new InvalidDataException($"Preset '{preset.Name}' entry count {preset.Entries.Count} is out of range.");
            }
            foreach (var entry in preset.Entries)
            {
                if (entry.UnitId == 0 || entry.Count is < 1 or > 50 || entry.Rank is < 0 or > 3)
                {
                    throw new InvalidDataException($"Preset '{preset.Name}' entry '{entry.DisplayName}' carries illegal values.");
                }
            }
        }
    }

    // ---------- Primitives ----------

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteString(Stream stream, string value, int maxBytes)
    {
        if (value.Length == 0 || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException("String field must be non-empty and free of NUL bytes.");
        }
        var bytes = Utf8.GetBytes(value);
        if (bytes.Length > maxBytes)
        {
            throw new InvalidDataException($"String field exceeds the {maxBytes}-byte UTF-8 limit.");
        }
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteOptionalString(Stream stream, string? value, int maxBytes)
    {
        if (value is null)
        {
            stream.WriteByte(0);
            return;
        }
        stream.WriteByte(1);
        WriteString(stream, value, maxBytes);
    }

    private static string ReadString(ref Reader reader, int maxBytes)
    {
        var length = reader.ReadUInt32();
        if (length is 0 || length > (uint)maxBytes)
        {
            throw new InvalidDataException($"String length {length} is out of the 1..{maxBytes} byte range.");
        }
        var bytes = reader.ReadBytes((int)length);
        string value;
        try
        {
            value = Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("String field is not strict UTF-8.", ex);
        }
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException("String field must not contain NUL bytes.");
        }
        return value;
    }

    private static string? ReadOptionalString(ref Reader reader, int maxBytes)
    {
        var flag = reader.ReadByte();
        return flag switch
        {
            0 => null,
            1 => ReadString(ref reader, maxBytes),
            _ => throw new InvalidDataException($"Unknown optional-string flag {flag}."),
        };
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _payload;
        private int _offset;

        public Reader(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            _offset = 0;
        }

        public byte ReadByte()
        {
            if (_offset + 1 > _payload.Length)
            {
                throw new InvalidDataException("Payload truncated.");
            }
            return _payload[_offset++];
        }

        public ushort ReadUInt16()
        {
            if (_offset + 2 > _payload.Length)
            {
                throw new InvalidDataException("Payload truncated.");
            }
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(_offset, 2));
            _offset += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            if (_offset + 4 > _payload.Length)
            {
                throw new InvalidDataException("Payload truncated.");
            }
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public ulong ReadUInt64()
        {
            if (_offset + 8 > _payload.Length)
            {
                throw new InvalidDataException("Payload truncated.");
            }
            var value = BinaryPrimitives.ReadUInt64LittleEndian(_payload.Slice(_offset, 8));
            _offset += 8;
            return value;
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || _offset + count > _payload.Length)
            {
                throw new InvalidDataException("Payload truncated.");
            }
            var slice = _payload.Slice(_offset, count);
            _offset += count;
            return slice;
        }

        public void RequireEnd()
        {
            if (_offset != _payload.Length)
            {
                throw new InvalidDataException($"Payload carries {_payload.Length - _offset} trailing byte(s).");
            }
        }
    }
}
