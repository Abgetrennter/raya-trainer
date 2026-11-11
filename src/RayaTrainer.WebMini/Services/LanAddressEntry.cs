namespace RayaTrainer.WebMini.Services;

/// <summary>一条可用的局域网地址（显示名 + IP）。</summary>
public sealed record LanAddressEntry(string DisplayName, string IpAddress);
