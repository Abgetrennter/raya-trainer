using System.Threading.Channels;

namespace RayaTrainer.App.Web.State;

public interface IGameStateBroadcaster
{
    ChannelReader<TrainerWebStateMessage> Subscribe(CancellationToken cancellationToken = default);

    void Publish(TrainerWebStateMessage message);

    void StartPolling(
        Func<TrainerGameStateResponse?> gameStateProvider,
        Func<TrainerSelectedUnitResponse?> selectedUnitProvider,
        Func<TrainerFeaturesResponse?> featuresProvider);

    void StopPolling();
}
