using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RayaTrainer.App.Services;
using RayaTrainer.App.Web.Auth;
using RayaTrainer.App.Web.State;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.Web;

public sealed class TrainerWebHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TrainerWebHost(WebApplication app)
    {
        _app = app;
    }

    public static TrainerWebHost Create(
        ITrainerSessionService session,
        TrainerManifest manifest,
        IGameApiCommandQueue? commandQueue = null,
        TrainerAppSettingsStore? settingsStore = null,
        ITrainerPresetSource? presetSource = null,
        ITrainerSavedPresetSource? savedPresetSource = null,
        FeatureStateCoordinator? featureStateCoordinator = null,
        int port = TrainerWebEndpointDefaults.Port)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(manifest);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "Web", "wwwroot")
        });

        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Services.AddSingleton(session);
        builder.Services.AddSingleton(commandQueue ?? new GameApiCommandQueue());
        var resolvedSettingsStore = settingsStore ?? new TrainerAppSettingsStore();
        builder.Services.AddSingleton(resolvedSettingsStore);
        if (presetSource is not null)
        {
            builder.Services.AddSingleton(presetSource);
        }
        builder.Services.AddSingleton(savedPresetSource ?? new TrainerSavedPresetFileSource());
        if (featureStateCoordinator is not null)
        {
            builder.Services.AddSingleton(featureStateCoordinator);
        }
        builder.Services.AddSingleton(CreateWebFeatures(manifest));
        builder.Services.AddSingleton<DevicePairingTokenStore>();
        builder.Services.AddSingleton<IDeviceApprovalService, WpfDeviceApprovalService>();
        builder.Services.AddSingleton<IGameStateBroadcaster, GameStateBroadcaster>();
        builder.Services.AddSingleton<TrainerApiHandler>();

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseWebSockets();
        app.MapTrainerApiEndpoints();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var handler = app.Services.GetRequiredService<TrainerApiHandler>();
            var broadcaster = app.Services.GetRequiredService<IGameStateBroadcaster>();
            broadcaster.StartPolling(
                () => handler.GetGameState(),
                () => handler.ReadSelectedUnit(),
                () => handler.GetFeatures());
        });

        return new TrainerWebHost(app);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _app.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var broadcaster = (IGameStateBroadcaster?)_app.Services.GetService(typeof(IGameStateBroadcaster));
        broadcaster?.StopPolling();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _app.StopAsync(cancellation.Token).ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static IReadOnlyList<TrainerFeature> CreateWebFeatures(TrainerManifest manifest)
    {
        return TrainerFeatureCatalog.CreateGridFeatures(manifest.Features).ToArray();
    }
}
