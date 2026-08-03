namespace RayaTrainer.App.Services;

public interface IMobileRemoteAvailability
{
    bool IsAvailable { get; }

    string UnavailableReason { get; }
}
