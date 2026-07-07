using System.Text;
using RayaTrainer.Core.Agent;

namespace RayaTrainer.ApiGenerator;

public sealed record GeneratedDirectGameApiFile(string RelativePath, string Content);

public static class DirectGameApiShadowGenerator
{
    private static readonly IReadOnlyDictionary<string, int> FieldSizes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["AgentStatusCode"] = 2,
            ["bool"] = 4,
            ["float"] = 4,
            ["GameApiDispatchStatus"] = 4,
            ["uint"] = 4,
            ["ushort"] = 2
        };

    public static IReadOnlyList<GeneratedDirectGameApiFile> Generate(DirectGameApiCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var diagnostics = DirectGameApiCatalogValidator.Validate(catalog);
        if (diagnostics.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, diagnostics));
        }

        var files = new List<GeneratedDirectGameApiFile>();
        foreach (var api in catalog.Apis)
        {
            files.Add(new GeneratedDirectGameApiFile(
                $"RayaTrainer.Core/Agent/Generated/{RequestClass(api)}.generated.cs",
                GenerateCSharpRequest(api)));
            files.Add(new GeneratedDirectGameApiFile(
                $"RayaTrainer.Core/Agent/Generated/{PayloadClass(api)}.generated.cs",
                GenerateCSharpPayload(api)));
        }

        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Core/Agent/Generated/IAgentGameApiClient.generated.cs",
            GenerateCSharpClientInterface(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Core/Agent/Generated/AgentNamedPipeClient.GameApi.generated.cs",
            GenerateCSharpNamedPipeClient(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Core/Features/Generated/IAgentFeatureController.generated.cs",
            GenerateCSharpFeatureControllerInterface(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Core/Features/Generated/AgentFeatureController.GameApi.generated.cs",
            GenerateCSharpFeatureController(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Agent/Generated/AgentProtocol.GameApi.generated.h",
            GenerateCppProtocol(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Agent/Generated/AgentGameApi.Declarations.generated.h",
            GenerateCppDeclarations(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Agent/Generated/AgentGameApi.Dispatch.generated.inc",
            GenerateCppDispatch(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Agent/Generated/AgentGameApi.NativeRouting.generated.h",
            GenerateCppNativeRouting(catalog.Apis)));
        files.Add(new GeneratedDirectGameApiFile(
            "RayaTrainer.Agent/Generated/AgentPipeServer.Dispatch.generated.inc",
            GenerateCppPipeServerDispatch(catalog.Apis)));

        return files;
    }

    private static string GenerateCppNativeRouting(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("#pragma once");
        sb.AppendLine();
        sb.AppendLine("#include <cstdint>");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer::agent");
        sb.AppendLine("{");
        sb.AppendLine("enum class GeneratedGameApiRoute : uint8_t");
        sb.AppendLine("{");
        sb.AppendLine("    Bootstrap = 0,");
        sb.AppendLine("    Native = 1");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("constexpr GeneratedGameApiRoute kGeneratedGameApiRoutes[] = {");
        foreach (var api in apis)
        {
            sb.AppendLine($"    GeneratedGameApiRoute::{api.Implementation}, // {api.Name}");
        }
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("constexpr uint32_t kGeneratedGameApiRouteCount =");
        sb.AppendLine("    static_cast<uint32_t>(sizeof(kGeneratedGameApiRoutes) / sizeof(kGeneratedGameApiRoutes[0]));");
        var nativeBitmap = apis
            .Select((api, index) => api.Implementation == DirectGameApiImplementation.Native ? 1UL << index : 0UL)
            .Aggregate(0UL, (current, bit) => current | bit);
        sb.AppendLine($"constexpr uint64_t kGeneratedNativeGameApiBitmap = 0x{nativeBitmap:X16}ull;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static IReadOnlyList<GeneratedDirectGameApiFile> WriteToDirectory(
        DirectGameApiCatalog catalog,
        string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        }

        var files = Generate(catalog);
        foreach (var file in files)
        {
            var outputPath = Path.Combine(
                outputRoot,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException($"Unable to resolve output directory for {outputPath}."));
            File.WriteAllText(outputPath, file.Content, Encoding.UTF8);
        }

        return files;
    }

    private static string GenerateCSharpRequest(DirectGameApiDefinition api)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("using System.Buffers.Binary;");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer.Core.Agent;");
        sb.AppendLine();
        AppendRecordDeclaration(sb, $"public sealed record {RequestClass(api)}", api.RequestFields);
        sb.AppendLine("{");
        sb.AppendLine($"    public const int Size = {SizeOf(api.RequestFields)};");
        sb.AppendLine();
        sb.AppendLine("    public byte[] Encode()");
        sb.AppendLine("    {");
        foreach (var field in api.RequestFields.Where(field => field.RequiredNonZero))
        {
            sb.AppendLine($"        if ({field.Name} == 0)");
            sb.AppendLine("        {");
            sb.AppendLine($"            throw new InvalidDataException(\"{api.PipeCommand.Name} {field.Name} cannot be zero.\");");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("        if (TimeoutMilliseconds == 0 || TimeoutMilliseconds > 5000)");
        sb.AppendLine("        {");
        sb.AppendLine($"            throw new InvalidDataException(\"{api.PipeCommand.Name} API timeout must be between 1 and 5000 milliseconds.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var buffer = new byte[Size];");
        var offset = 0;
        foreach (var field in api.RequestFields)
        {
            sb.AppendLine($"        {CSharpWrite(field, offset)}");
            offset += SizeOf(field);
        }

        sb.AppendLine("        return buffer;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCSharpPayload(DirectGameApiDefinition api)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("using System.Buffers.Binary;");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer.Core.Agent;");
        sb.AppendLine();
        AppendRecordDeclaration(sb, $"public readonly record struct {PayloadClass(api)}", api.PayloadFields);
        sb.AppendLine("{");
        sb.AppendLine($"    public const int Size = {SizeOf(api.PayloadFields)};");
        if (api.Name.Equals("SetSelectedStatusBit", StringComparison.Ordinal))
        {
            sb.AppendLine("    private const int LegacySize = 12;");
        }

        sb.AppendLine();
        sb.AppendLine("    public static byte[] Encode(");
        for (var i = 0; i < api.PayloadFields.Count; i++)
        {
            var field = api.PayloadFields[i];
            var suffix = i + 1 == api.PayloadFields.Count ? ")" : ",";
            sb.AppendLine($"        {CSharpType(field.Type)} {ToCamelCase(field.Name)}{suffix}");
        }

        sb.AppendLine("    {");
        sb.AppendLine("        var buffer = new byte[Size];");
        var writeOffset = 0;
        foreach (var field in api.PayloadFields)
        {
            sb.AppendLine($"        {CSharpWritePayloadValue(field, writeOffset, ToCamelCase(field.Name))}");
            writeOffset += SizeOf(field);
        }

        sb.AppendLine("        return buffer;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public static {PayloadClass(api)} ReadFrom(ReadOnlyMemory<byte> payload)");
        sb.AppendLine("    {");
        if (api.Name.Equals("SetSelectedStatusBit", StringComparison.Ordinal))
        {
            sb.AppendLine("        if (payload.Length == LegacySize)");
            sb.AppendLine("        {");
            sb.AppendLine("            var legacySpan = payload.Span;");
            sb.AppendLine($"            return new {PayloadClass(api)}(");
            sb.AppendLine("                (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(legacySpan.Slice(0, sizeof(ushort))),");
            sb.AppendLine("                BinaryPrimitives.ReadUInt16LittleEndian(legacySpan.Slice(2, sizeof(ushort))),");
            sb.AppendLine("                (GameApiDispatchStatus)BinaryPrimitives.ReadUInt32LittleEndian(legacySpan.Slice(4, sizeof(uint))),");
            sb.AppendLine("                BinaryPrimitives.ReadUInt32LittleEndian(legacySpan.Slice(8, sizeof(uint))),");
            sb.AppendLine("                GameThreadTickBefore: 0,");
            sb.AppendLine("                GameThreadTickAfter: 0);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("        if (payload.Length != Size)");
        sb.AppendLine("        {");
        sb.AppendLine($"            throw new InvalidDataException($\"Agent {api.PipeCommand.Name} payload must be {{Size}} bytes, actual {{payload.Length}}.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var span = payload.Span;");
        sb.AppendLine($"        return new {PayloadClass(api)}(");

        var reads = new List<string>();
        var offset = 0;
        foreach (var field in api.PayloadFields)
        {
            reads.Add(CSharpRead(field, offset));
            offset += SizeOf(field);
        }

        for (var i = 0; i < reads.Count; i++)
        {
            var suffix = i + 1 == reads.Count ? ");" : ",";
            sb.AppendLine($"            {reads[i]}{suffix}");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCSharpClientInterface(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer.Core.Agent;");
        sb.AppendLine();
        sb.AppendLine("public interface IAgentGameApiClient");
        sb.AppendLine("{");
        foreach (var api in apis)
        {
            sb.AppendLine($"    Task<{PayloadClass(api)}> {api.PipeCommand.ClientMethod}(");
            sb.AppendLine("        int processId,");
            sb.AppendLine($"        {RequestClass(api)} request,");
            sb.AppendLine("        TimeSpan timeout,");
            sb.AppendLine("        CancellationToken cancellationToken = default);");
            sb.AppendLine();
        }

        TrimTrailingBlankLine(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCSharpNamedPipeClient(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer.Core.Agent;");
        sb.AppendLine();
        sb.AppendLine("public sealed partial class AgentNamedPipeClient");
        sb.AppendLine("{");
        foreach (var api in apis)
        {
            sb.AppendLine($"    public Task<{PayloadClass(api)}> {api.PipeCommand.ClientMethod}(");
            sb.AppendLine("        int processId,");
            sb.AppendLine($"        {RequestClass(api)} request,");
            sb.AppendLine("        TimeSpan timeout,");
            sb.AppendLine("        CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        return SendCommandAsync(");
            sb.AppendLine("            processId,");
            sb.AppendLine($"            AgentCommand.{api.PipeCommand.Name},");
            sb.AppendLine("            request.Encode(),");
            sb.AppendLine("            timeout,");
            sb.AppendLine($"            {PayloadClass(api)}.ReadFrom,");
            sb.AppendLine("            cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        TrimTrailingBlankLine(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCSharpFeatureController(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("using System;");
        sb.AppendLine("using RayaTrainer.Core.Agent;");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer.Core.Features;");
        sb.AppendLine();
        sb.AppendLine("public sealed partial class AgentFeatureController");
        sb.AppendLine("{");
        foreach (var api in apis.Where(api => api.FeatureMethod.Generate))
        {
            sb.AppendLine($"    public {api.FeatureMethod.ReturnType} {api.FeatureMethod.Name}({FeatureParameters(api.FeatureMethod.Parameters)})");
            sb.AppendLine("    {");
            sb.AppendLine("        EnsureDirectGameApiSupported();");
            sb.AppendLine("        var effectiveTimeout = timeout ?? GameApiCommandTimeout;");
            sb.AppendLine("        var gameApiTimeoutMilliseconds = Math.Clamp((uint)effectiveTimeout.TotalMilliseconds, 1u, 5000u);");
            sb.AppendLine($"        var request = new {RequestClass(api)}(");
            var arguments = RequestConstructorArguments(api);
            for (var i = 0; i < arguments.Count; i++)
            {
                var suffix = i + 1 == arguments.Count ? ");" : ",";
                sb.AppendLine($"            {arguments[i]}{suffix}");
            }

            sb.AppendLine($"        var result = _client.{api.PipeCommand.ClientMethod}(_processId, request, effectiveTimeout)");
            sb.AppendLine("            .GetAwaiter().GetResult();");
            sb.AppendLine("        if (result.StatusCode != AgentStatusCode.Ok &&");
            sb.AppendLine("            result.StatusCode != AgentStatusCode.TimedOut)");
            sb.AppendLine("        {");
            sb.AppendLine("            throw new InvalidOperationException(");
            sb.AppendLine($"                $\"Agent {api.PipeCommand.Name} failed: status={{result.StatusCode}}, dispatch={{result.DispatchStatus}}.\");");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        return {FeatureReturnExpression(api)};");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        TrimTrailingBlankLine(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCSharpFeatureControllerInterface(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("using System;");
        sb.AppendLine("using RayaTrainer.Core.Agent;");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer.Core.Features;");
        sb.AppendLine();
        sb.AppendLine("public interface IAgentFeatureController : ITrainerFeatureController");
        sb.AppendLine("{");
        sb.AppendLine("    bool SupportsDirectGameApi { get; }");
        sb.AppendLine();
        foreach (var api in apis.Where(api => api.FeatureMethod.Generate))
        {
            if (!string.IsNullOrWhiteSpace(api.FeatureMethod.DocSummary))
            {
                sb.AppendLine("    /// <summary>");
                sb.AppendLine($"    /// {api.FeatureMethod.DocSummary}");
                sb.AppendLine("    /// </summary>");
            }

            if (!string.IsNullOrWhiteSpace(api.FeatureMethod.DocReturn))
            {
                sb.AppendLine($"    /// <returns>{api.FeatureMethod.DocReturn}</returns>");
            }

            sb.AppendLine($"    {api.FeatureMethod.ReturnType} {api.FeatureMethod.Name}({FeatureParameters(api.FeatureMethod.Parameters)});");
            sb.AppendLine();
        }

        TrimTrailingBlankLine(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCppProtocol(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("#pragma once");
        sb.AppendLine();
        sb.AppendLine("#include <cstdint>");
        sb.AppendLine();
        sb.AppendLine("namespace RayaTrainer::agent");
        sb.AppendLine("{");
        sb.AppendLine("#pragma pack(push, 1)");
        foreach (var api in apis)
        {
            AppendCppStruct(sb, RequestClass(api), api.RequestFields);
            AppendCppStruct(sb, PayloadClass(api), api.PayloadFields);
        }

        sb.AppendLine("#pragma pack(pop)");
        sb.AppendLine();
        foreach (var api in apis)
        {
            sb.AppendLine($"static_assert(sizeof({RequestClass(api)}) == {SizeOf(api.RequestFields)});");
            sb.AppendLine($"static_assert(sizeof({PayloadClass(api)}) == {SizeOf(api.PayloadFields)});");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCppDeclarations(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("// Generated GameApi declarations");
        foreach (var api in apis)
        {
            sb.AppendLine($"AgentStatusCode {api.PipeCommand.Name}FromPayload(");
            sb.AppendLine("    const unsigned char* payload,");
            sb.AppendLine("    uint32_t length,");
            sb.AppendLine($"    {PayloadClass(api)}& result);");
            sb.AppendLine();
        }

        TrimTrailingBlankLine(sb);
        return sb.ToString();
    }

    private static string GenerateCppDispatch(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("// Generated GameApi implementations");
        sb.AppendLine("bool TryReadFloat(const unsigned char* payload, uint32_t length, uint32_t& offset, float& value)");
        sb.AppendLine("{");
        sb.AppendLine("    uint32_t bits = 0;");
        sb.AppendLine("    if (!TryReadUInt32(payload, length, offset, bits)) return false;");
        sb.AppendLine("    std::memcpy(&value, &bits, sizeof(value));");
        sb.AppendLine("    return true;");
        sb.AppendLine("}");
        sb.AppendLine();
        foreach (var api in apis)
        {
            AppendCppTryReadRequest(sb, api);
            AppendCppFromPayload(sb, api);
        }

        TrimTrailingBlankLine(sb);
        return sb.ToString();
    }

    private static string GenerateCppPipeServerDispatch(IReadOnlyList<DirectGameApiDefinition> apis)
    {
        var sb = NewGeneratedSource();
        sb.AppendLine("// Generated pipe server dispatch");
        foreach (var api in apis)
        {
            sb.AppendLine($"if (command == AgentCommand::{api.PipeCommand.Name})");
            sb.AppendLine("{");
            sb.AppendLine($"    {PayloadClass(api)} result = {{}};");
            sb.AppendLine($"    {api.PipeCommand.Name}FromPayload(payload.data(), header.PayloadLength, result);");
            sb.AppendLine("    WriteGameApiResponse(pipe, header, result);");
            sb.AppendLine("    return;");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        TrimTrailingBlankLine(sb);
        return sb.ToString();
    }

    private static void AppendCppTryReadRequest(StringBuilder sb, DirectGameApiDefinition api)
    {
        sb.AppendLine($"bool TryRead{api.RequestType}Request(");
        sb.AppendLine("    const unsigned char* payload,");
        sb.AppendLine("    uint32_t length,");
        sb.AppendLine($"    {RequestClass(api)}& request)");
        sb.AppendLine("{");
        sb.AppendLine($"    if (length != sizeof({RequestClass(api)})) return false;");
        sb.AppendLine("    uint32_t offset = 0;");
        var reads = api.RequestFields.Select(field => CppReadRequestField(field)).ToArray();
        if (reads.Length == 0)
        {
            sb.AppendLine("    return offset == length;");
        }
        else
        {
            sb.AppendLine($"    if (!({reads[0]}");
            for (var i = 1; i < reads.Length; i++)
            {
                sb.AppendLine($"        && {reads[i]}");
            }

            sb.AppendLine("        && offset == length))");
            sb.AppendLine("    {");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");
        }

        foreach (var field in api.RequestFields.Where(field => field.RequiredNonZero))
        {
            sb.AppendLine();
            sb.AppendLine($"    if (request.{field.Name} == 0)");
            sb.AppendLine("    {");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");
        }

        sb.AppendLine();
        sb.AppendLine("    return true;");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendCppFromPayload(StringBuilder sb, DirectGameApiDefinition api)
    {
        sb.AppendLine($"AgentStatusCode {api.PipeCommand.Name}FromPayload(");
        sb.AppendLine("    const unsigned char* payload,");
        sb.AppendLine("    uint32_t length,");
        sb.AppendLine($"    {PayloadClass(api)}& result)");
        sb.AppendLine("{");
        sb.AppendLine($"    {RequestClass(api)} request = {{}};");
        sb.AppendLine($"    if (!TryRead{api.RequestType}Request(payload, length, request))");
        sb.AppendLine("    {");
        sb.AppendLine("        result = {};");
        sb.AppendLine("        result.StatusCode = static_cast<uint16_t>(AgentStatusCode::InvalidCommand);");
        sb.AppendLine("        result.AgentVersion = kAgentProtocolVersion;");
        sb.AppendLine("        result.DispatchStatus = static_cast<uint32_t>(GameApiDispatchStatus::Failed);");
        sb.AppendLine("        return AgentStatusCode::InvalidCommand;");
        sb.AppendLine("    }");
        sb.AppendLine($"    return DispatchNative{api.Name}(request, result);");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string CppReadRequestField(DirectGameApiField field)
    {
        return field.Type.Equals("float", StringComparison.OrdinalIgnoreCase)
            ? $"TryReadFloat(payload, length, offset, request.{field.Name})"
            : $"TryReadUInt32(payload, length, offset, request.{field.Name})";
    }

    private static void AppendCppStruct(
        StringBuilder sb,
        string name,
        IReadOnlyList<DirectGameApiField> fields)
    {
        sb.AppendLine($"struct {name}");
        sb.AppendLine("{");
        foreach (var field in fields)
        {
            sb.AppendLine($"    {CppType(field.Type)} {field.Name};");
        }

        sb.AppendLine("};");
        sb.AppendLine();
    }

    private static void AppendRecordDeclaration(
        StringBuilder sb,
        string declaration,
        IReadOnlyList<DirectGameApiField> fields)
    {
        sb.AppendLine($"{declaration}(");
        for (var i = 0; i < fields.Count; i++)
        {
            var suffix = i + 1 == fields.Count ? ")" : ",";
            sb.AppendLine($"    {CSharpType(fields[i].Type)} {fields[i].Name}{suffix}");
        }
    }

    private static IReadOnlyList<string> RequestConstructorArguments(DirectGameApiDefinition api)
    {
        var arguments = new List<string>();
        foreach (var field in api.RequestFields)
        {
            if (field.Name.Equals("TimeoutMilliseconds", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("TimeoutMilliseconds: gameApiTimeoutMilliseconds");
                continue;
            }

            if (field.Name.Equals("EnableDirectGameApi", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("EnableDirectGameApi: true");
                continue;
            }

            var parameter = api.FeatureMethod.Parameters.SingleOrDefault(parameter =>
                parameter.MapsTo?.Equals(field.Name, StringComparison.OrdinalIgnoreCase) == true);
            if (parameter is null)
            {
                throw new InvalidDataException($"{api.Name}.{field.Name} has no feature parameter mapping.");
            }

            arguments.Add($"{field.Name}: {parameter.Name}");
        }

        return arguments;
    }

    private static string FeatureParameters(IReadOnlyList<DirectGameApiFeatureParameter> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
            parameter.Default is null
                ? $"{parameter.Type} {parameter.Name}"
                : $"{parameter.Type} {parameter.Name} = {parameter.Default}"));
    }

    private static string FeatureReturnExpression(DirectGameApiDefinition api)
    {
        if (api.FeatureMethod.ReturnField is null)
        {
            return "result";
        }

        return api.FeatureMethod.ReturnType.Equals("bool", StringComparison.OrdinalIgnoreCase)
            ? $"result.{api.FeatureMethod.ReturnField} != 0"
            : $"result.{api.FeatureMethod.ReturnField}";
    }

    private static string RequestClass(DirectGameApiDefinition api)
    {
        return $"AgentGameApi{api.RequestType}Request";
    }

    private static string PayloadClass(DirectGameApiDefinition api)
    {
        return $"AgentGameApi{api.PayloadType}Payload";
    }

    private static string CSharpWrite(DirectGameApiField field, int offset)
    {
        return field.Type.ToLowerInvariant() switch
        {
            "bool" => $"BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan({offset}, sizeof(uint)), {field.Name} ? 1u : 0u);",
            "float" => $"BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan({offset}, sizeof(float)), {field.Name});",
            "uint" => $"BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan({offset}, sizeof(uint)), {field.Name});",
            _ => throw new InvalidDataException($"Unsupported request field type {field.Type}.")
        };
    }

    private static string CSharpWritePayloadValue(DirectGameApiField field, int offset, string value)
    {
        return field.Type.ToLowerInvariant() switch
        {
            "agentstatuscode" => $"BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan({offset}, sizeof(ushort)), (ushort){value});",
            "float" => $"BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan({offset}, sizeof(float)), {value});",
            "gameapidispatchstatus" => $"BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan({offset}, sizeof(uint)), (uint){value});",
            "uint" => $"BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan({offset}, sizeof(uint)), {value});",
            "ushort" => $"BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan({offset}, sizeof(ushort)), {value});",
            _ => throw new InvalidDataException($"Unsupported payload field type {field.Type}.")
        };
    }

    private static string CSharpRead(DirectGameApiField field, int offset)
    {
        return field.Type.ToLowerInvariant() switch
        {
            "agentstatuscode" => $"(AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice({offset}, sizeof(ushort)))",
            "float" => $"BinaryPrimitives.ReadSingleLittleEndian(span.Slice({offset}, sizeof(float)))",
            "gameapidispatchstatus" => $"(GameApiDispatchStatus)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice({offset}, sizeof(uint)))",
            "uint" => $"BinaryPrimitives.ReadUInt32LittleEndian(span.Slice({offset}, sizeof(uint)))",
            "ushort" => $"BinaryPrimitives.ReadUInt16LittleEndian(span.Slice({offset}, sizeof(ushort)))",
            _ => throw new InvalidDataException($"Unsupported payload field type {field.Type}.")
        };
    }

    private static int SizeOf(IReadOnlyList<DirectGameApiField> fields)
    {
        return fields.Sum(SizeOf);
    }

    private static int SizeOf(DirectGameApiField field)
    {
        return FieldSizes.TryGetValue(field.Type, out var size)
            ? size
            : throw new InvalidDataException($"Unsupported field type {field.Type}.");
    }

    private static string CSharpType(string type)
    {
        return type;
    }

    private static string CppType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "agentstatuscode" => "uint16_t",
            "bool" => "uint32_t",
            "float" => "float",
            "gameapidispatchstatus" => "uint32_t",
            "uint" => "uint32_t",
            "ushort" => "uint16_t",
            _ => throw new InvalidDataException($"Unsupported C++ field type {type}.")
        };
    }

    private static string ToCamelCase(string value)
    {
        return value.Length == 0
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static StringBuilder NewGeneratedSource()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated from apis.json - DO NOT EDIT MANUALLY");
        return sb;
    }

    private static void TrimTrailingBlankLine(StringBuilder sb)
    {
        while (sb.Length >= Environment.NewLine.Length * 2 &&
               sb.ToString(sb.Length - Environment.NewLine.Length * 2, Environment.NewLine.Length * 2) == Environment.NewLine + Environment.NewLine)
        {
            sb.Length -= Environment.NewLine.Length;
        }
    }
}

public static class DirectGameApiCatalogValidator
{
    public static IReadOnlyList<string> Validate(DirectGameApiCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var diagnostics = new List<string>();
        if (catalog.Version != 3)
        {
            diagnostics.Add($"Direct GameApi catalog version must be 3, actual {catalog.Version}.");
        }

        AddDuplicates(diagnostics, catalog.Apis, api => api.PipeCommand.Name, "pipe command name");
        AddDuplicates(diagnostics, catalog.Apis, api => api.PipeCommand.Value, "pipe command value");
        AddDuplicates(diagnostics, catalog.Apis, api => api.PipeCommand.ClientMethod, "pipe client method");

        foreach (var api in catalog.Apis)
        {
            if (!Enum.TryParse<AgentCommand>(api.PipeCommand.Name, out var command))
            {
                diagnostics.Add($"{api.Name}: AgentCommand.{api.PipeCommand.Name} is not defined.");
                continue;
            }

            var commandValue = Convert.ToInt32(command);
            if (commandValue != api.PipeCommand.Value)
            {
                diagnostics.Add(
                    $"{api.Name}: AgentCommand.{api.PipeCommand.Name} is {commandValue}, metadata declares {api.PipeCommand.Value}.");
            }
        }

        return diagnostics;
    }

    private static void AddDuplicates<TKey>(
        List<string> diagnostics,
        IReadOnlyList<DirectGameApiDefinition> apis,
        Func<DirectGameApiDefinition, TKey> keySelector,
        string label)
        where TKey : notnull
    {
        foreach (var group in apis.GroupBy(keySelector).Where(group => group.Count() > 1))
        {
            diagnostics.Add($"Duplicate Direct GameApi {label} {group.Key}: {string.Join(", ", group.Select(api => api.Name))}.");
        }
    }
}
