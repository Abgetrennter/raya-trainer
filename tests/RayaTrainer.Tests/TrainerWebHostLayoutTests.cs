using Xunit;
using RayaTrainer.App.Services;
using RayaTrainer.App.Web;

namespace RayaTrainer.Tests;

public sealed class TrainerWebHostLayoutTests
{
    [Fact]
    public void AppProjectEnablesAspNetCoreAndShipsLocalWebAssets()
    {
        var root = RepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "RayaTrainer.App.csproj"));

        Assert.Contains("<FrameworkReference Include=\"Microsoft.AspNetCore.App\" />", project, StringComparison.Ordinal);
        Assert.Contains("Web\\wwwroot\\**\\*", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AppStartupOwnsMainWindowAndWebHostLifecycle()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "App.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "App.xaml.cs"));

        Assert.DoesNotContain("StartupUri=", xaml, StringComparison.Ordinal);
        Assert.Contains("protected override async void OnStartup", app, StringComparison.Ordinal);
        Assert.Contains("TrainerWebHost", app, StringComparison.Ordinal);
        Assert.Contains("var settingsStore = new TrainerAppSettingsStore()", app, StringComparison.Ordinal);
        Assert.Contains("settingsStore:", app, StringComparison.Ordinal);
        Assert.Contains("presetSource: viewModel", app, StringComparison.Ordinal);
        Assert.Contains("protected override async void OnExit", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MinimalApiMapsRemoteControlEndpoints()
    {
        var root = RepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "TrainerApiEndpoints.cs"));

        Assert.Contains("MapGet(\"/status\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/diagnostics\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/toggles/{featureId}\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/resources\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/reinforcements/execute\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/secret-protocols/grant\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/presets\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/reinforcements/queue/execute\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/secret-protocols/queue/grant\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/pair\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/ws\"", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteReadEndpointsRequirePairingToken()
    {
        var root = RepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "TrainerApiEndpoints.cs"));

        Assert.Contains("RequireAuthorized(context, tokenStore)", ExtractMapBlock(endpoints, "MapGet(\"/status\""));
        Assert.Contains("RequireAuthorized(context, tokenStore)", ExtractMapBlock(endpoints, "MapGet(\"/diagnostics\""));
        Assert.Contains("RequireAuthorized(context, tokenStore)", ExtractMapBlock(endpoints, "MapGet(\"/features\""));
        Assert.Contains("RequireAuthorized(context, tokenStore)", ExtractMapBlock(endpoints, "MapGet(\"/presets\""));
        Assert.Contains("RequireAuthorized(context, tokenStore)", ExtractMapBlock(endpoints, "MapGet(\"/selected-unit\""));
    }

    [Fact]
    public void MobileFrontendUsesLocalVueRuntime()
    {
        var root = RepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "index.html"));

        Assert.Contains("vendor/vue.global.prod.js", index, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn", index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileActionGridShowsCapabilityAndDisablesParameterizedActions()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "TrainerWebHost.cs"));
        var contracts = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "TrainerWebContracts.cs"));
        var index = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "index.html"));

        Assert.DoesNotContain("CreatePanelActions", host, StringComparison.Ordinal);
        Assert.Contains("bool RequiresParameters", contracts, StringComparison.Ordinal);
        Assert.Contains("f.capabilityState !== 'Ready'", index, StringComparison.Ordinal);
        Assert.Contains("f.requiresParameters", index, StringComparison.Ordinal);
        Assert.Contains("capabilityHint", index, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileFrontendLoadsDesktopSavedPresetsForReinforcementAndSecretProtocols()
    {
        var root = RepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "index.html"));
        var app = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "app.js"));

        Assert.Contains("loadPresets", app, StringComparison.Ordinal);
        Assert.Contains("/api/presets", app, StringComparison.Ordinal);
        Assert.Contains("secretProtocolPresets", app, StringComparison.Ordinal);
        Assert.Contains("selectedReinforcementPresetName", app, StringComparison.Ordinal);
        Assert.Contains("selectedSecretProtocolPresetName", app, StringComparison.Ordinal);
        Assert.Contains("applySecretProtocolPreset", app, StringComparison.Ordinal);
        Assert.Contains("appendSecretProtocolPreset", app, StringComparison.Ordinal);
        Assert.Contains("toReinforcementQueueEntries", app, StringComparison.Ordinal);
        Assert.Contains("executeReinforcementQueue", app, StringComparison.Ordinal);
        Assert.Contains("grantSecretProtocolQueue", app, StringComparison.Ordinal);
        Assert.Contains("v-model=\"selectedReinforcementPresetName\"", index, StringComparison.Ordinal);
        Assert.Contains("v-model=\"selectedSecretProtocolPresetName\"", index, StringComparison.Ordinal);
        Assert.Contains("应用预设", index, StringComparison.Ordinal);
        Assert.Contains("当前入队", index, StringComparison.Ordinal);
        Assert.Contains("执行队列", index, StringComparison.Ordinal);
        Assert.Contains("清空队列", index, StringComparison.Ordinal);
        Assert.Contains("追加预设", index, StringComparison.Ordinal);
        Assert.Contains("授予列表", index, StringComparison.Ordinal);
        Assert.Contains("清空列表", index, StringComparison.Ordinal);
        Assert.DoesNotContain("授予整套预设", index, StringComparison.Ordinal);
        Assert.DoesNotContain("grantSecretProtocolQueue(toSecretProtocolQueueEntries(preset))", index, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileFrontendPairsBeforeReadingProtectedRemoteState()
    {
        var root = RepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "app.js"));
        var mounted = ExtractMethodBlock(app, "async mounted()");

        Assert.True(
            mounted.IndexOf("await this.pairDevice()", StringComparison.Ordinal) <
            mounted.IndexOf("await this.refreshAll()", StringComparison.Ordinal));
    }

    [Fact]
    public void MobileFrontendRePairsAfterWebSocketPolicyViolation()
    {
        var root = RepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "app.js"));
        var onCloseBlock = ExtractEventHandlerBlock(app, "socket.onclose =");

        Assert.Contains("event.code === 1008", onCloseBlock, StringComparison.Ordinal);
        Assert.Contains("await this.handleUnauthorized()", onCloseBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileFrontendStopsProtectedReadsAfterUnauthorizedGet()
    {
        var root = RepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "app.js"));

        Assert.Contains("return null;", ExtractMethodBlock(app, "async authorizedGet(url)"));
        Assert.Contains("if (!response) return;", ExtractMethodBlock(app, "async refreshStatus()"));
        Assert.Contains("if (!response) return;", ExtractMethodBlock(app, "async loadFeatures()"));
        Assert.Contains("if (!response) return;", ExtractMethodBlock(app, "async loadPresets()"));
    }

    [Fact]
    public void MobileFrontendValidatesNumericInputsBeforePosting()
    {
        var root = RepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "app.js"));

        Assert.Contains("parseRequiredNumber", app, StringComparison.Ordinal);
        Assert.Contains("return null;", ExtractMethodBlock(app, "parseRequiredNumber(value, label)"));
        Assert.Contains("if (unitId === null) return;", ExtractMethodBlock(app, "async executeReinforcement()"));
        Assert.Contains("if (playerTechId === null || upgradeId === null) return;", ExtractMethodBlock(app, "async grantSecretProtocol()"));
    }

    [Fact]
    public void WebHostPollsAndBroadcastsFeatureState()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "TrainerWebHost.cs"));
        var broadcaster = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "State", "GameStateBroadcaster.cs"));
        var contract = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "State", "IGameStateBroadcaster.cs"));

        Assert.Contains("() => handler.GetFeatures()", host, StringComparison.Ordinal);
        Assert.Contains("featuresProvider", contract, StringComparison.Ordinal);
        Assert.Contains("TrainerWebStateMessage.FeaturesUpdate", broadcaster, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileFrontendDoesNotExposeTemplateReplacementByDefault()
    {
        var root = RepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "index.html"));

        Assert.DoesNotContain("模板替换", index, StringComparison.Ordinal);
        Assert.DoesNotContain("replaceTemplateModel", index, StringComparison.Ordinal);
        Assert.DoesNotContain("replaceTemplateWeapon", index, StringComparison.Ordinal);
    }

    [Fact]
    public void WebManifestReferencesExistingPngIcons()
    {
        var root = RepositoryRoot();
        var webRoot = Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot");
        var manifest = File.ReadAllText(Path.Combine(webRoot, "manifest.json"));

        Assert.Contains("/icons/icon-192.png", manifest, StringComparison.Ordinal);
        Assert.Contains("/icons/icon-512.png", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(webRoot, "icons", "icon-192.png")));
        Assert.True(File.Exists(Path.Combine(webRoot, "icons", "icon-512.png")));
    }

    [Fact]
    public async Task TrainerWebHostCreateUsesBuilderCreationOptionsForHostPaths()
    {
        using var session = new TrainerSessionManager();

        await using var host = TrainerWebHost.Create(session, TestAssets.LoadManifest());

        Assert.NotNull(host);
    }

    [Fact]
    public void CatalogEndpointsRequireAuthorization()
    {
        var root = RepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "TrainerApiEndpoints.cs"));

        Assert.Contains("RequireAuthorized(context, tokenStore)", ExtractMapBlock(endpoints, "MapGet(\"/reinforcements/catalog\""));
        Assert.Contains("RequireAuthorized(context, tokenStore)", ExtractMapBlock(endpoints, "MapGet(\"/secret-protocols/catalog\""));
    }

    [Fact]
    public void MobileCatalogPickerOverlayExists()
    {
        var root = RepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.App", "Web", "wwwroot", "index.html"));

        Assert.Contains("picker-overlay", index, StringComparison.Ordinal);
        Assert.Contains("openPicker('reinforcement')", index, StringComparison.Ordinal);
        Assert.Contains("openPicker('protocol')", index, StringComparison.Ordinal);
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

    private static string ExtractMapBlock(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Unable to find marker: {marker}");

        var nextMap = source.IndexOf("api.Map", start + marker.Length, StringComparison.Ordinal);
        var end = nextMap >= 0 ? nextMap : source.Length;
        return source[start..end];
    }

    private static string ExtractMethodBlock(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Unable to find marker: {marker}");

        var nextMethod = source.IndexOf("\n    async ", start + marker.Length, StringComparison.Ordinal);
        var end = nextMethod >= 0 ? nextMethod : source.Length;
        return source[start..end];
    }

    private static string ExtractEventHandlerBlock(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Unable to find marker: {marker}");

        var nextHandler = source.IndexOf("\n      socket.", start + marker.Length, StringComparison.Ordinal);
        var end = nextHandler >= 0 ? nextHandler : source.Length;
        return source[start..end];
    }
}
