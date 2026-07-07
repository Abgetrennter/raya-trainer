using RayaTrainer.App.Web.Auth;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class DevicePairingTokenStoreTests
{
    [Fact]
    public void IssuedTokenValidatesBearerHeader()
    {
        var store = new DevicePairingTokenStore();

        var token = store.IssueToken();

        Assert.True(token.Length >= 43);
        Assert.True(store.ValidateBearer($"Bearer {token}"));
        Assert.False(store.ValidateBearer("Bearer invalid-token"));
    }

    [Fact]
    public void ClearRevokesIssuedTokens()
    {
        var store = new DevicePairingTokenStore();
        var token = store.IssueToken();

        store.Clear();

        Assert.False(store.ValidateBearer($"Bearer {token}"));
    }

    [Fact]
    public async Task InMemoryApprovalReturnsConfiguredDecision()
    {
        var allowed = new InMemoryDeviceApprovalService(approved: true);
        var denied = new InMemoryDeviceApprovalService(approved: false);
        var request = new DeviceApprovalRequest("phone", "mobile browser", "127.0.0.1");

        Assert.True(await allowed.ApproveAsync(request));
        Assert.False(await denied.ApproveAsync(request));
    }
}
