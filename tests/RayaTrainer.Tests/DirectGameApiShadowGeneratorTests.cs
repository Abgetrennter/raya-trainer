using RayaTrainer.ApiGenerator;
using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class DirectGameApiShadowGeneratorTests
{
    [Fact]
    public void GenerateProducesExpectedShadowFileSet()
    {
        var catalog = LoadCatalog();
        var files = DirectGameApiShadowGenerator.Generate(catalog);

        var paths = files.Select(file => file.RelativePath).Order(StringComparer.Ordinal).ToArray();
        var expectedPaths = catalog.Apis
            .SelectMany(api => new[]
            {
                $"RayaTrainer.Core/Agent/Generated/AgentGameApi{api.PayloadType}Payload.generated.cs",
                $"RayaTrainer.Core/Agent/Generated/AgentGameApi{api.RequestType}Request.generated.cs"
            })
            .Concat(
            [
                "RayaTrainer.Agent/Generated/AgentGameApi.Declarations.generated.h",
                "RayaTrainer.Agent/Generated/AgentGameApi.Dispatch.generated.inc",
                "RayaTrainer.Agent/Generated/AgentGameApi.NativeRouting.generated.h",
                "RayaTrainer.Agent/Generated/AgentPipeServer.Dispatch.generated.inc",
                "RayaTrainer.Agent/Generated/AgentProtocol.GameApi.generated.h",
                "RayaTrainer.Core/Agent/Generated/AgentNamedPipeClient.GameApi.generated.cs",
                "RayaTrainer.Core/Agent/Generated/IAgentGameApiClient.generated.cs",
                "RayaTrainer.Core/Features/Generated/IAgentFeatureController.generated.cs",
                "RayaTrainer.Core/Features/Generated/AgentFeatureController.GameApi.generated.cs"
            ])
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedPaths, paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("src/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateMatchesCSharpPilotProtocolShape()
    {
        var files = DirectGameApiShadowGenerator.Generate(LoadCatalog());

        var createUnitRequest = Content(files, "RayaTrainer.Core/Agent/Generated/AgentGameApiCreateUnitRequest.generated.cs");
        Assert.Contains("public sealed record AgentGameApiCreateUnitRequest(", createUnitRequest, StringComparison.Ordinal);
        Assert.Contains("public const int Size = 24;", createUnitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, sizeof(uint)), ThingClassAddress);", createUnitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(4, sizeof(float)), PosX);", createUnitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(8, sizeof(float)), PosY);", createUnitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(12, sizeof(float)), PosZ);", createUnitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, sizeof(uint)), TimeoutMilliseconds);", createUnitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20, sizeof(uint)), EnableDirectGameApi ? 1u : 0u);", createUnitRequest, StringComparison.Ordinal);

        var getThingClassRequest = Content(files, "RayaTrainer.Core/Agent/Generated/AgentGameApiGetThingClassRequest.generated.cs");
        Assert.Contains("if (UnitTypeId == 0)", getThingClassRequest, StringComparison.Ordinal);

        var getThingClassPayload = Content(files, "RayaTrainer.Core/Agent/Generated/AgentGameApiGetThingClassPayload.generated.cs");
        Assert.Contains("public const int Size = 28;", getThingClassPayload, StringComparison.Ordinal);
        Assert.Contains("uint UnitTypeId,", getThingClassPayload, StringComparison.Ordinal);
        Assert.Contains("uint ThingClassAddress,", getThingClassPayload, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, sizeof(uint)))", getThingClassPayload, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, sizeof(uint)))", getThingClassPayload, StringComparison.Ordinal);
        Assert.Contains("(GameApiDispatchStatus)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, sizeof(uint)))", getThingClassPayload, StringComparison.Ordinal);

        var setSelectedStatusBitRequest = Content(files, "RayaTrainer.Core/Agent/Generated/AgentGameApiSetSelectedStatusBitRequest.generated.cs");
        Assert.Contains("public sealed record AgentGameApiSetSelectedStatusBitRequest(", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("uint Domain,", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("uint BitIndex,", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("uint Enabled,", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("public const int Size = 20;", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, sizeof(uint)), Domain);", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, sizeof(uint)), BitIndex);", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, sizeof(uint)), Enabled);", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, sizeof(uint)), TimeoutMilliseconds);", setSelectedStatusBitRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, sizeof(uint)), EnableDirectGameApi ? 1u : 0u);", setSelectedStatusBitRequest, StringComparison.Ordinal);

        var setSelectedStatusBitPayload = Content(files, "RayaTrainer.Core/Agent/Generated/AgentGameApiSetSelectedStatusBitPayload.generated.cs");
        Assert.Contains("private const int LegacySize = 12;", setSelectedStatusBitPayload, StringComparison.Ordinal);
        Assert.Contains("if (payload.Length == LegacySize)", setSelectedStatusBitPayload, StringComparison.Ordinal);
        Assert.Contains("GameThreadTickBefore: 0,", setSelectedStatusBitPayload, StringComparison.Ordinal);

        var setSelectedUnitHealthRequest = Content(files, "RayaTrainer.Core/Agent/Generated/AgentGameApiSetSelectedUnitHealthRequest.generated.cs");
        Assert.Contains("public sealed record AgentGameApiSetSelectedUnitHealthRequest(", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("uint Mode,", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("float Health,", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("float MaxHealth,", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("public const int Size = 20;", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, sizeof(uint)), Mode);", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(4, sizeof(float)), Health);", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(8, sizeof(float)), MaxHealth);", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, sizeof(uint)), TimeoutMilliseconds);", setSelectedUnitHealthRequest, StringComparison.Ordinal);
        Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, sizeof(uint)), EnableDirectGameApi ? 1u : 0u);", setSelectedUnitHealthRequest, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateMatchesCppPilotProtocolShape()
    {
        var files = DirectGameApiShadowGenerator.Generate(LoadCatalog());

        var protocol = Content(files, "RayaTrainer.Agent/Generated/AgentProtocol.GameApi.generated.h");
        Assert.Contains("struct AgentGameApiCreateUnitRequest", protocol, StringComparison.Ordinal);
        Assert.Contains("float PosX;", protocol, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(AgentGameApiCreateUnitRequest) == 24);", protocol, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(AgentGameApiGrantSecretProtocolPayload) == 20);", protocol, StringComparison.Ordinal);
        Assert.Contains("struct AgentGameApiSetSelectedStatusBitRequest", protocol, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(AgentGameApiSetSelectedStatusBitRequest) == 20);", protocol, StringComparison.Ordinal);

        var declarations = Content(files, "RayaTrainer.Agent/Generated/AgentGameApi.Declarations.generated.h");
        Assert.Contains("AgentStatusCode GrantSecretProtocolFromPayload(", declarations, StringComparison.Ordinal);

        var dispatch = Content(files, "RayaTrainer.Agent/Generated/AgentGameApi.Dispatch.generated.inc");
        Assert.Contains("bool TryReadCreateUnitRequest(", dispatch, StringComparison.Ordinal);
        Assert.Contains("return DispatchNativeCreateUnit(request, result);", dispatch, StringComparison.Ordinal);
        Assert.Contains("bool TryReadGrantSecretProtocolRequest(", dispatch, StringComparison.Ordinal);
        Assert.Contains("bool TryReadSetSelectedStatusBitRequest(", dispatch, StringComparison.Ordinal);
        Assert.Contains("return DispatchNativeSetSelectedStatusBit(request, result);", dispatch, StringComparison.Ordinal);
        Assert.Contains("bool TryReadSetSelectedUnitHealthRequest(", dispatch, StringComparison.Ordinal);
        Assert.Contains("return DispatchNativeSetSelectedUnitHealth(request, result);", dispatch, StringComparison.Ordinal);

        var pipeServer = Content(files, "RayaTrainer.Agent/Generated/AgentPipeServer.Dispatch.generated.inc");
        Assert.Contains("if (command == AgentCommand::GrantSecretProtocol)", pipeServer, StringComparison.Ordinal);
        Assert.Contains("WriteGameApiResponse(pipe, header, result);", pipeServer, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratePreservesFeatureControllerBehavioralExceptions()
    {
        var files = DirectGameApiShadowGenerator.Generate(LoadCatalog());

        var featureController = Content(files, "RayaTrainer.Core/Features/Generated/AgentFeatureController.GameApi.generated.cs");

        Assert.Contains("public bool HasUpgrade(uint upgradeHash, TimeSpan? timeout = null)", featureController, StringComparison.Ordinal);
        Assert.Contains("return result.HasUpgrade != 0;", featureController, StringComparison.Ordinal);
        Assert.DoesNotContain("public uint ReadSelectedUnitCode(", featureController, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateClampsGameApiMailboxTimeoutSeparatelyFromPipeTimeout()
    {
        var files = DirectGameApiShadowGenerator.Generate(LoadCatalog());

        var featureController = Content(files, "RayaTrainer.Core/Features/Generated/AgentFeatureController.GameApi.generated.cs");

        Assert.Contains("var gameApiTimeoutMilliseconds = Math.Clamp((uint)effectiveTimeout.TotalMilliseconds, 1u, 5000u);", featureController, StringComparison.Ordinal);
        Assert.Contains("TimeoutMilliseconds: gameApiTimeoutMilliseconds", featureController, StringComparison.Ordinal);
        Assert.Contains("SetSelectedStatusBitAsync(_processId, request, effectiveTimeout)", featureController, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateProducesAgentFeatureControllerInterfaceFromCatalog()
    {
        var files = DirectGameApiShadowGenerator.Generate(LoadCatalog());

        var agentInterface = Content(files, "RayaTrainer.Core/Features/Generated/IAgentFeatureController.generated.cs");
        var featureController = Content(files, "RayaTrainer.Core/Features/Generated/AgentFeatureController.GameApi.generated.cs");

        Assert.Contains("public interface IAgentFeatureController : ITrainerFeatureController", agentInterface, StringComparison.Ordinal);
        Assert.Contains("bool SupportsDirectGameApi { get; }", agentInterface, StringComparison.Ordinal);
        Assert.Contains("EnsureDirectGameApiSupported();", featureController, StringComparison.Ordinal);
        Assert.Contains("uint GetThingClass(uint unitTypeId, TimeSpan? timeout = null);", agentInterface, StringComparison.Ordinal);
        Assert.Contains("GameApiDispatchStatus TriggerLevelUp(uint count = 1, uint rank = 0, uint flags = 0, TimeSpan? timeout = null);", agentInterface, StringComparison.Ordinal);
        Assert.Contains("uint CreateUnit(uint thingClassAddress, float posX, float posY, float posZ, TimeSpan? timeout = null);", agentInterface, StringComparison.Ordinal);
        Assert.Contains("GameApiDispatchStatus GrantSecretProtocol(uint techHash, uint upgradeHash, TimeSpan? timeout = null);", agentInterface, StringComparison.Ordinal);
        Assert.Contains("GameApiDispatchStatus SetSelectedStatusBit(uint domain, uint bitIndex, uint enabled, TimeSpan? timeout = null);", agentInterface, StringComparison.Ordinal);
        Assert.Contains("GameApiDispatchStatus SetSelectedUnitHealth(uint mode, float health, float maxHealth, TimeSpan? timeout = null);", agentInterface, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadSelectedUnitCode", agentInterface, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateRejectsPipeCommandValueMismatch()
    {
        var catalog = LoadCatalog();
        var apis = catalog.Apis
            .Select(api => api.Name == "CreateUnit"
                ? api with { PipeCommand = api.PipeCommand with { Value = 99 } }
                : api)
            .ToArray();
        var invalidCatalog = catalog with { Apis = apis };

        var exception = Assert.Throws<InvalidDataException>(() => DirectGameApiShadowGenerator.Generate(invalidCatalog));

        Assert.Contains("AgentCommand.CreateUnit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteToDirectoryCreatesShadowFilesUnderRequestedRoot()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "RayaTrainer-api-generator-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var files = DirectGameApiShadowGenerator.WriteToDirectory(LoadCatalog(), outputRoot);

            Assert.Contains(files, file => file.RelativePath == "RayaTrainer.Agent/Generated/AgentProtocol.GameApi.generated.h");
            Assert.True(File.Exists(Path.Combine(
                outputRoot,
                "RayaTrainer.Agent",
                "Generated",
                "AgentProtocol.GameApi.generated.h")));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void VerifyCurrentRepositoryFindsGeneratedSourcesAndBootstrapContractInSync()
    {
        var diagnostics = DirectGameApiGenerationVerifier.VerifyRepository(RepositoryRoot());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void VerifyGeneratedSourcesReportsDrift()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "RayaTrainer-api-generator-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var catalog = LoadCatalog();
            DirectGameApiShadowGenerator.WriteToDirectory(catalog, outputRoot);
            File.AppendAllText(
                Path.Combine(
                    outputRoot,
                    "RayaTrainer.Core",
                    "Agent",
                    "Generated",
                    "IAgentGameApiClient.generated.cs"),
                "// drift");

            var diagnostics = DirectGameApiGenerationVerifier.VerifyGeneratedSources(catalog, outputRoot);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("Generated source drift", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static DirectGameApiCatalog LoadCatalog()
    {
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot(), "src", "RayaTrainer.Core", "Agent", "apis.json"));
        return DirectGameApiCatalog.Load(stream);
    }

    private static string Content(IReadOnlyList<GeneratedDirectGameApiFile> files, string relativePath)
    {
        return files.Single(file => file.RelativePath == relativePath).Content;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
