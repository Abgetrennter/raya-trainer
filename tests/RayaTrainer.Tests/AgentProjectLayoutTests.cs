using System.Xml.Linq;
using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentProjectLayoutTests
{
    [Fact]
    public void NativeAgentProjectBuildsAsWin32DynamicLibrary()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "src", "RayaTrainer.Agent", "RayaTrainer.Agent.vcxproj");

        var document = XDocument.Load(projectPath);
        var namespaceName = document.Root!.Name.Namespace;
        var compileIncludes = document
            .Descendants(namespaceName + "ClCompile")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        var releaseWin32 = document
            .Descendants(namespaceName + "PropertyGroup")
            .Single(element => string.Equals(
                element.Attribute("Condition")?.Value,
                "'$(Configuration)|$(Platform)'=='Release|Win32'",
                StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Label")?.Value, "Configuration", StringComparison.Ordinal));

        Assert.Equal("DynamicLibrary", releaseWin32.Element(namespaceName + "ConfigurationType")?.Value);
        Assert.Equal("v143", releaseWin32.Element(namespaceName + "PlatformToolset")?.Value);
        Assert.Equal("Unicode", releaseWin32.Element(namespaceName + "CharacterSet")?.Value);
        Assert.Equal("false", releaseWin32.Element(namespaceName + "VcpkgEnabled")?.Value);
        Assert.Contains("AgentMemoryAccess.cpp", compileIncludes);
        Assert.Contains("AgentGameApi.cpp", compileIncludes);

        var releaseCompile = document
            .Descendants(namespaceName + "ItemDefinitionGroup")
            .Single(element => string.Equals(
                element.Attribute("Condition")?.Value,
                "'$(Configuration)|$(Platform)'=='Release|Win32'",
                StringComparison.Ordinal))
            .Element(namespaceName + "ClCompile");
        Assert.Equal("MultiThreaded", releaseCompile?.Element(namespaceName + "RuntimeLibrary")?.Value);
    }

    [Fact]
    public void NativeAgentProtocolConstantsMatchManagedProtocol()
    {
        var headerPath = Path.Combine(RepositoryRoot(), "src", "RayaTrainer.Agent", "AgentProtocol.h");

        var header = File.ReadAllText(headerPath);

        Assert.Contains($"kAgentMagic = 0x{AgentProtocol.Magic:X8}u", header);
        Assert.Contains($"kAgentProtocolVersion = {AgentProtocol.Version}", header);
        Assert.Contains($"kAgentBuildFingerprint = 0x{AgentBuildIdentity.Fingerprint:X16}ull", header);
        Assert.Contains($"Ping = {(ushort)AgentCommand.Ping}", header);
        Assert.Contains($"ReadMemory = {(ushort)AgentCommand.ReadMemory}", header);
        Assert.Contains($"SmokeGetThingClass = {(ushort)AgentCommand.SmokeGetThingClass}", header);
        Assert.Contains($"SetNativeCatalog = {(ushort)AgentCommand.SetNativeCatalog}", header);
        Assert.Contains($"ExpandProductionQueue = {(ushort)AgentCommand.ExpandProductionQueue}", header);
        Assert.Contains($"Ok = {(ushort)AgentStatusCode.Ok}", header);
    }

    [Fact]
    public void NativeAgentGameApiCatalogSupportsRuntimeNativeCatalog()
    {
        var repoRoot = RepositoryRoot();
        var header = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentGameApi.h"));
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentGameApi.cpp"));

        // Runtime catalog contract: the DLL must expose init/set/resolve accessors that let the
        // host override the compile-time 1.12 RVAs via SetNativeCatalog.
        Assert.Contains("void InitializeNativeCatalog()", header, StringComparison.Ordinal);
        Assert.Contains("bool HasNativeCatalog()", header, StringComparison.Ordinal);
        Assert.Contains("AgentStatusCode SetNativeCatalogFromPayload", header, StringComparison.Ordinal);
        Assert.Contains("uint32_t ResolveNativeCatalogRva(NativeCatalogEntry entry)", header, StringComparison.Ordinal);

        // The native catalog entry order contract must be present in the protocol header.
        var protocolHeader = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentProtocol.h"));
        Assert.Contains("GameClientPointer = 0", protocolHeader, StringComparison.Ordinal);
        Assert.Contains("GetThingClass = 1", protocolHeader, StringComparison.Ordinal);
        Assert.Contains("LevelUpSelected = 2", protocolHeader, StringComparison.Ordinal);
        Assert.Contains("CreateUnit = 3", protocolHeader, StringComparison.Ordinal);
        Assert.Contains("KillUnit = 4", protocolHeader, StringComparison.Ordinal);

        // The pipe server must dispatch SetNativeCatalog before the generated GameApi chain,
        // and initialize the catalog on worker-thread startup.
        var pipeServer = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentPipeServer.cpp"));
        Assert.Contains("InitializeNativeCatalog()", pipeServer, StringComparison.Ordinal);
        Assert.Contains("AgentCommand::SetNativeCatalog", pipeServer, StringComparison.Ordinal);
        Assert.Contains("SetNativeCatalogFromPayload", pipeServer, StringComparison.Ordinal);

        // TryReadGameMode must resolve the GameClient pointer via the runtime catalog, not a
        // bare compile-time constant, so the shell check works on non-1.12 profiles.
        Assert.Contains("ResolveNativeCatalogRva(NativeCatalogEntry::GameClientPointer)", source, StringComparison.Ordinal);
        Assert.Contains("if (gameClientRva == 0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (rvas[index] == 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAgentGameApiCatalogStartsDisabledAndRequiresGameThread()
    {
        var repoRoot = RepositoryRoot();
        var header = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentGameApi.h"));
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentGameApi.cpp"));

        Assert.Contains("GameApiFunctionSpec", header, StringComparison.Ordinal);
        Assert.Contains("EnabledByDefault", header, StringComparison.Ordinal);
        Assert.Contains("GameThread", header, StringComparison.Ordinal);
        Assert.Contains("GetThingClass", source, StringComparison.Ordinal);
        Assert.Contains("0x3E4230u", source, StringComparison.Ordinal);
        Assert.Contains("CreateUnit", source, StringComparison.Ordinal);
        Assert.Contains("0x205240u", source, StringComparison.Ordinal);
        Assert.Contains("LevelUpSelected", source, StringComparison.Ordinal);
        Assert.Contains("0x35C200u", source, StringComparison.Ordinal);
        Assert.Contains("KillUnit", source, StringComparison.Ordinal);
        Assert.Contains("0x39EA50u", source, StringComparison.Ordinal);
        Assert.Contains("GameApiThreadRequirement::GameThread", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnabledByDefault = true", source, StringComparison.Ordinal);
        Assert.Contains("return smokeVerified && isGameThread", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeDispatcherOwnsGameThreadDispatch()
    {
        var repoRoot = RepositoryRoot();
        var dispatcher = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentGameThreadDispatcher.cpp"));
        var hooks = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Agent", "AgentNativeHooks.cpp"));

        Assert.Contains("AgentGameThreadDispatcher::Pump", dispatcher, StringComparison.Ordinal);
        Assert.Contains("AgentGameThreadDispatcher::Pump", hooks, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreProjectEmbedsOnlyCurrentRuntimeManifest()
    {
        var repoRoot = RepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "src", "RayaTrainer.Core", "RayaTrainer.Core.csproj");
        var document = XDocument.Load(projectPath);
        var includes = document
            .Descendants("EmbeddedResource")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.Contains(@"Assets\trainer_report.json", includes);
        Assert.DoesNotContain(includes, include => include.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LuaConsoleInjectorProjectBuildsForWinX86()
    {
        var projectPath = Path.Combine(
            RepositoryRoot(),
            "tools",
            "Ra3LuaConsole",
            "injector",
            "Ra3LuaConsole.Injector.csproj");

        var document = XDocument.Load(projectPath);
        var propertyGroups = document.Descendants("PropertyGroup").ToArray();

        Assert.Contains(propertyGroups, group =>
            group.Element("RuntimeIdentifier")?.Value == "win-x86");
        Assert.Contains(propertyGroups, group =>
            group.Element("RuntimeIdentifiers")?.Value == "win-x86");
        Assert.Contains(propertyGroups, group =>
            group.Element("PlatformTarget")?.Value == "x86");
    }

    [Fact]
    public void LuaConsoleInjectionVerificationScriptUsesRepoRootAndWinX86Injector()
    {
        var scriptPath = Path.Combine(
            RepositoryRoot(),
            "tools",
            "Ra3LuaConsole",
            "test-dll",
            "verify-injection.ps1");

        var script = File.ReadAllText(scriptPath);

        Assert.Contains(@"Join-Path $scriptDir ""..\..\..""", script, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Join-Path $scriptDir ""..\..\..\..""", script, StringComparison.Ordinal);
        Assert.Contains("--runtime win-x86", script, StringComparison.Ordinal);
        Assert.Contains("--configuration Release", script, StringComparison.Ordinal);
        Assert.Contains("Test-ProcessNameMatch", script, StringComparison.Ordinal);
        Assert.Contains("To-ComparableProcessName", script, StringComparison.Ordinal);
        Assert.Contains("$runDll = Join-Path $testDllDirectory", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath $testDll -Destination $runDll -Force", script, StringComparison.Ordinal);
        Assert.Contains(@"""--dll"" $runDll", script, StringComparison.Ordinal);
        Assert.All(script, character => Assert.True(
            character <= 0x7F,
            "PowerShell 5.1 treats UTF-8 without BOM as ANSI; keep this script ASCII-only."));
    }

    [Fact]
    public void LuaConsoleDllProjectIncludesPlannedRuntimeComponents()
    {
        var repoRoot = RepositoryRoot();
        var projectPath = Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "Ra3LuaConsole.Dll.vcxproj");

        var document = XDocument.Load(projectPath);
        var namespaceName = document.Root!.Name.Namespace;
        var compileIncludes = document
            .Descendants(namespaceName + "ClCompile")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var headerIncludes = document
            .Descendants(namespaceName + "ClInclude")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var releaseLink = document
            .Descendants(namespaceName + "ItemDefinitionGroup")
            .Single(element => string.Equals(
                element.Attribute("Condition")?.Value,
                "'$(Configuration)|$(Platform)'=='Release|Win32'",
                StringComparison.Ordinal))
            .Element(namespaceName + "Link");

        string[] requiredSources =
        [
            @"lua\LuaExecutor.cpp",
            @"lua\RequestQueue.cpp",
            @"hooks\HookInstaller.cpp",
            @"hooks\UpdateHook.cpp",
            @"mcp\JsonRpc.cpp",
            @"mcp\McpHandlers.cpp",
            @"mcp\McpServer.cpp",
            @"vendor\minhook\src\buffer.c",
            @"vendor\minhook\src\hook.c",
            @"vendor\minhook\src\trampoline.c",
            @"vendor\minhook\src\hde\hde32.c"
        ];
        string[] requiredHeaders =
        [
            @"lua\LuaTypes.h",
            @"lua\LuaExecutor.h",
            @"lua\RequestQueue.h",
            @"hooks\HookInstaller.h",
            @"hooks\UpdateHook.h",
            @"mcp\JsonRpc.h",
            @"mcp\McpHandlers.h",
            @"mcp\McpServer.h",
            @"mcp\McpTypes.h",
            @"vendor\minhook\include\MinHook.h"
        ];

        foreach (var source in requiredSources)
        {
            Assert.Contains(source, compileIncludes);
        }

        foreach (var header in requiredHeaders)
        {
            Assert.Contains(header, headerIncludes);
        }

        Assert.Contains(
            "ws2_32.lib",
            releaseLink?.Element(namespaceName + "AdditionalDependencies")?.Value ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolSourcesAreNotHiddenByCatchAllGitIgnore()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));
        var patterns = File.ReadAllLines(Path.Combine(RepositoryRoot(), ".gitignore"));

        Assert.DoesNotContain("/tools/*", patterns);
        Assert.Contains("/tools/corona/", patterns);
        Assert.Contains("/tools/diag/", patterns);
        Assert.Contains("/tools/parse_trainer.py", patterns);
        Assert.Contains("!/tools/Ra3LuaConsole/dll/vendor/", gitignore, StringComparison.Ordinal);
        Assert.Contains("!/tools/Ra3LuaConsole/dll/vendor/**", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaConsoleMcpVerificationScriptUsesMcpSmokeChecks()
    {
        var scriptPath = Path.Combine(
            RepositoryRoot(),
            "tools",
            "Ra3LuaConsole",
            "test-dll",
            "verify-mcp.ps1");

        var script = File.ReadAllText(scriptPath);

        Assert.Contains(@"Join-Path $scriptDir ""..\..\..""", script, StringComparison.Ordinal);
        Assert.Contains("--runtime win-x86", script, StringComparison.Ordinal);
        Assert.Contains("--configuration Release", script, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:$Port", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-McpRequest", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-WebRequest", script, StringComparison.Ordinal);
        Assert.DoesNotContain("curl.exe", script, StringComparison.Ordinal);
        Assert.Contains("initialize", script, StringComparison.Ordinal);
        Assert.Contains("tools/list", script, StringComparison.Ordinal);
        Assert.Contains("resolve_offset", script, StringComparison.Ordinal);
        Assert.Contains("read_memory", script, StringComparison.Ordinal);
        Assert.Contains("list_selected_units", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$RequireLua", script, StringComparison.Ordinal);
        Assert.Contains("game update hook not installed", script, StringComparison.Ordinal);
        Assert.Contains("eval_lua", script, StringComparison.Ordinal);
        Assert.All(script, character => Assert.True(
            character <= 0x7F,
            "PowerShell 5.1 treats UTF-8 without BOM as ANSI; keep this script ASCII-only."));
    }

    [Fact]
    public void LuaConsoleBuildsLuaApiFromNormalizedAddresses()
    {
        var sourcePath = Path.Combine(
            RepositoryRoot(),
            "tools",
            "Ra3LuaConsole",
            "dll",
            "lua",
            "LuaExecutor.cpp");

        var source = File.ReadAllText(sourcePath);

        Assert.Contains("NormalizeGameAddress(address)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return reinterpret_cast<T>(address)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaConsoleSelectedUnitToolUsesVerifiedSelectionOffsets()
    {
        var repoRoot = RepositoryRoot();
        var offsetsHeader = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "config",
            "GameOffsets.h"));
        var handlers = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "mcp",
            "McpHandlers.cpp"));

        Assert.Contains("gameSelection", offsetsHeader, StringComparison.Ordinal);
        Assert.Contains("0xCDB73C", offsetsHeader, StringComparison.Ordinal);
        Assert.Contains("selectionCount", handlers, StringComparison.Ordinal);
        Assert.Contains("gameObject", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("not implemented: selected-unit traversal address is not verified yet", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaConsoleResolveOffsetKeepsStructOffsetsUnnormalized()
    {
        var handlers = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "Ra3LuaConsole",
            "dll",
            "mcp",
            "McpHandlers.cpp"));

        Assert.Contains("isStructOffset", handlers, StringComparison.Ordinal);
        Assert.Contains("address = offsets.luaStateOffset", handlers, StringComparison.Ordinal);
        Assert.Contains("isStructOffset ? address : NormalizeGameAddress(address)", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaConsoleDoesNotPollLuaRequestsFromDllWorkerThread()
    {
        var repoRoot = RepositoryRoot();
        var dllMain = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "DllMain.cpp"));
        var handlers = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "mcp",
            "McpHandlers.cpp"));

        Assert.DoesNotContain("Tick();", dllMain, StringComparison.Ordinal);
        Assert.DoesNotContain("void Tick()", File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "hooks",
            "UpdateHook.h")), StringComparison.Ordinal);
        Assert.Contains("game update hook not installed", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaConsoleUpdateHookUsesAbiNeutralTrampoline()
    {
        var updateHook = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "Ra3LuaConsole",
            "dll",
            "hooks",
            "UpdateHook.cpp"));

        Assert.Contains("__declspec(naked) void UpdateDetour()", updateHook, StringComparison.Ordinal);
        Assert.Contains("pushfd", updateHook, StringComparison.Ordinal);
        Assert.Contains("pushad", updateHook, StringComparison.Ordinal);
        Assert.Contains("call ProcessQueuedLuaRequests", updateHook, StringComparison.Ordinal);
        Assert.Contains("jmp dword ptr [g_originalUpdateTrampoline]", updateHook, StringComparison.Ordinal);
        Assert.DoesNotContain("m_originalUpdate(", updateHook, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaConsoleDllCopiesOffsetsConfigNextToDll()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "Ra3LuaConsole",
            "dll",
            "Ra3LuaConsole.Dll.vcxproj"));

        Assert.Contains("CopyOffsetsJson", project, StringComparison.Ordinal);
        Assert.Contains(@"..\injector\offsets.json", project, StringComparison.Ordinal);
        Assert.Contains("$(OutDir)offsets.json", project, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaConsoleReportsUpdateHookDiagnostics()
    {
        var repoRoot = RepositoryRoot();
        var hookHeader = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "hooks",
            "UpdateHook.h"));
        var hookSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "hooks",
            "UpdateHook.cpp"));
        var handlers = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "Ra3LuaConsole",
            "dll",
            "mcp",
            "McpHandlers.cpp"));

        Assert.Contains("struct UpdateHookSnapshot", hookHeader, StringComparison.Ordinal);
        Assert.Contains("Snapshot()", hookHeader, StringComparison.Ordinal);
        Assert.Contains("m_updateHits", hookHeader, StringComparison.Ordinal);
        Assert.Contains("GetCurrentThreadId()", hookSource, StringComparison.Ordinal);
        Assert.Contains("updateHits", handlers, StringComparison.Ordinal);
        Assert.Contains("updateThreadId", handlers, StringComparison.Ordinal);
        Assert.Contains("luaState", handlers, StringComparison.Ordinal);
        Assert.Contains("hookGameFrame", handlers, StringComparison.Ordinal);
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
