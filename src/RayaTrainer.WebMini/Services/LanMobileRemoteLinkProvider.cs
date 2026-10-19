using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RayaTrainer.Host.Services;

namespace RayaTrainer.WebMini.Services;

/// <summary>
/// 枚举本机可用局域网地址并生成遥控面板 URL，供启动时打印配对说明。
/// （从主程序 Services 迁移；主程序已不再需要 LAN 枚举。）
/// </summary>
public sealed class LanMobileRemoteLinkProvider
{
    private readonly int _port;

    public LanMobileRemoteLinkProvider(int port = TrainerWebEndpointDefaults.Port)
    {
        _port = port;
    }

    public string CreateRemoteUrl()
    {
        var first = GetAvailableAddresses().FirstOrDefault();
        return first is not null
            ? CreateRemoteUrl(first.IpAddress)
            : $"http://localhost:{_port}/";
    }

    public IReadOnlyList<LanAddressEntry> GetAvailableAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsUsableInterface)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses
                .Select(ua => new { Network = ni, Address = ua.Address }))
            .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(x.Address))
            .Select(x => new LanAddressEntry(
                $"{x.Address}（{x.Network.Name}）",
                x.Address.ToString()))
            .ToList();
    }

    public string CreateRemoteUrl(string ip)
    {
        return $"http://{ip}:{_port}/";
    }

    private static bool IsUsableInterface(NetworkInterface network)
    {
        return network.OperationalStatus == OperationalStatus.Up &&
            network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and
                not NetworkInterfaceType.Tunnel;
    }
}
