namespace RayaTrainer.App.Services;

public interface IMobileRemoteLinkProvider
{
    string CreateRemoteUrl();
    IReadOnlyList<LanAddressEntry> GetAvailableAddresses();
    string CreateRemoteUrl(string ip);
}
