using System.IO;
using System.Reflection;
using RayaTrainer.Core.Agent;

namespace RayaTrainer.Public.Tests;

public sealed class PublicShellContractsTests
{
    [Fact]
    public void SameProtocolDifferentBuildRemainsTakeoverCompatible()
    {
        var differentBuild = AgentBuildIdentity.BuildId ^ 1UL;

        Assert.Equal(
            AgentTakeoverDecision.DifferentBuild,
            AgentBuildIdentity.EvaluateTakeover(AgentProtocol.Version, differentBuild));
    }

    [Fact]
    public void PublicSolutionDoesNotReferencePrivateNativeProjects()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot(), "RayaTrainer.Public.sln"));

        Assert.Contains("RayaTrainer.Core", solution, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.App", solution, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.Host", solution, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.WebMini", solution, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.Public.Tests", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("RayaTrainer.Agent", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("RayaTrainer.ApiGenerator", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("RayaTrainer.AddressLint", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("RayaTrainer.Smoke", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("RayaTrainer.ContractLint", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBuildTreatsAgentDllAsOptionalArtifact()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.App",
            "RayaTrainer.App.csproj"));

        Assert.DoesNotContain("RayaTrainer.Agent.vcxproj", project, StringComparison.Ordinal);
        Assert.Contains("Condition=\"Exists('$(NativeAgentArtifact)')\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicAgentClientDoesNotExposeLegacyGenericControlMethods()
    {
        var publicMethods = typeof(IAgentClient)
            .GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("InstallPatchesAsync", publicMethods);
        Assert.DoesNotContain("ReadMemoryAsync", publicMethods);
        Assert.DoesNotContain("SetNativeCatalogAsync", publicMethods);
        Assert.DoesNotContain("GetMismatchDiagnosticsAsync", publicMethods);
        Assert.DoesNotContain("ScanSignaturesAsync", publicMethods);
        Assert.DoesNotContain("SendScriptOperationAsync", publicMethods);
        Assert.DoesNotContain("ListScriptOperationsAsync", publicMethods);
        Assert.DoesNotContain("DescribeScriptOperationAsync", publicMethods);
        Assert.DoesNotContain("InvokeScriptOperationAsync", publicMethods);
        Assert.DoesNotContain("ExecuteRecipePlanAsync", publicMethods);
    }

    [Fact]
    public void PublicGameApiSurfaceOmitsPrivatePointerOrLookupPrimitives()
    {
        var publicMethods = typeof(IAgentGameApiClient)
            .GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("SmokeGetThingClassAsync", publicMethods);
        Assert.DoesNotContain("CreateUnitAsync", publicMethods);
        Assert.DoesNotContain("GetCurrentPlayerAsync", publicMethods);
        Assert.DoesNotContain("LookupScienceByHashAsync", publicMethods);
        Assert.DoesNotContain("LookupTemplateByHashAsync", publicMethods);
        Assert.DoesNotContain("LookupUpgradeByHashAsync", publicMethods);
    }

    [Fact]
    public void TransparencyReportIsProjectedAndCoversRequiredSections()
    {
        var report = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "transparency-report.md"));
        Assert.Contains("混合源码说明", report, StringComparison.Ordinal);
        Assert.Contains("DLL 权限与联网声明", report, StringComparison.Ordinal);
        Assert.Contains("命令能力类别", report, StringComparison.Ordinal);
        Assert.Contains("agent-release.json", report, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", report, StringComparison.Ordinal);
        // Minimal disclosure: no concrete addresses / reverse-engineering evidence identifiers.
        Assert.DoesNotContain("RA3-Engine-Atlas", report, StringComparison.Ordinal);
        Assert.DoesNotContain("0x00", report, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicCoreBuildExcludesPrivateEvidenceAndLegacyWireDtos()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.Core",
            "RayaTrainer.Core.csproj"));

        Assert.Contains("<Compile Remove=\"Private\\**\\*.cs\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentMemoryReadRequest.cs", project, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentMismatchDiagnosticsPayload.cs", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicAgentSurfaceCarriesNoRawMemoryOrAddressParameters()
    {
        // Phase 4.2 invariant: the public remote surface (IAgentClient + inherited
        // IAgentGameApiClient) only exposes semantic, offline-gated operations. No method
        // parameter type may carry a raw memory address, pointer, RVA or byte-offset field,
        // and no parameter may be a native pointer type. Direct GameApi requests stay purely
        // semantic (unit codes, content hashes, values) — never host memory access.
        var forbiddenPropertyTokens = new[] { "Address", "Pointer", "Rva", "MemoryOffset", "ByteOffset" };
        var pointerTypes = new[] { typeof(IntPtr), typeof(UIntPtr) };

        var parameterTypes = typeof(IAgentClient)
            .GetMethods()
            .Concat(typeof(IAgentGameApiClient).GetMethods())
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.Namespace == "RayaTrainer.Core.Agent")
            .Distinct()
            .ToArray();

        Assert.NotEmpty(parameterTypes);

        foreach (var type in parameterTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(pointerTypes, pointer => pointer == property.PropertyType);
                foreach (var token in forbiddenPropertyTokens)
                {
                    Assert.False(
                        property.Name.Contains(token, StringComparison.Ordinal),
                        $"Public agent request {type.Name}.{property.Name} exposes a raw '{token}' field; " +
                        "host-memory/address access must stay behind the private boundary.");
                }
            }
        }
    }

    [Fact]
    public void PublicRuntimeCatalogContainsIdentityOnly()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.Core",
            "Agent",
            "Generated",
            "RuntimeCatalogMetadata.generated.cs"));

        Assert.Contains("CatalogContractHash", source, StringComparison.Ordinal);
        Assert.Contains("BuildId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignatureSymbolIds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LayoutFamilyIds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicAppBuildExcludesPrivateOperationExplorer()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.App",
            "RayaTrainer.App.csproj"));
        var mainWindow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.App",
            "MainWindow.xaml"));

        Assert.Contains("<Compile Remove=\"Private\\**\\*.cs\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Page Remove=\"Private\\**\\*.xaml\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain("OperationExplorerPage", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicManagedCatalogContainsMetadataButNoExecutionRecipe()
    {
        var catalog = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.Core",
            "Agent",
            "Generated",
            "ProductDefinition.catalog.generated.cs"));

        Assert.Contains("GeneratedProductDefinitionCatalog", catalog, StringComparison.Ordinal);
        Assert.Contains("GeneratedProductParameterDescriptor", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedProductRecipe", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("OperationCanonicalName", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("ScriptOperationKind", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicHostAndWebMiniStayWpfFreeAndProjectable()
    {
        // Web 可选组件两库必须在公开投影下独立可构建：
        // Host 排除 Private/**，两库均不得引入 WPF（Host 是无 UI 会话宿主，
        // WebMini 是最简单的原生 WinForms 单窗口，允许 WinForms、仍禁 WPF）。
        var hostProject = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "RayaTrainer.Host", "RayaTrainer.Host.csproj"));
        var webMiniProject = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "RayaTrainer.WebMini", "RayaTrainer.WebMini.csproj"));

        Assert.Contains("<Compile Remove=\"Private\\**\\*.cs\" />", hostProject, StringComparison.Ordinal);
        Assert.Contains("PublicProjection", hostProject, StringComparison.Ordinal);
        Assert.Contains("PublicProjection", webMiniProject, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWPF", hostProject, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWPF", webMiniProject, StringComparison.Ordinal);

        var hostSources = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src", "RayaTrainer.Host"),
            "*.cs",
            SearchOption.AllDirectories);
        foreach (var source in hostSources)
        {
            if (source.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(source);
            Assert.False(
                content.Contains("System.Windows", StringComparison.Ordinal),
                $"Host source {Path.GetFileName(source)} must not reference WPF types.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "RayaTrainer.Public.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Unable to locate the public solution root.");
    }
}
