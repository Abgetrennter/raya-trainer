namespace RayaTrainer.Core.Features;

public sealed record SecretProtocolGrantSettings(uint PlayerTechId, uint UpgradeId)
{
    public static readonly SecretProtocolGrantSettings Empty = new(0, 0);

    public bool HasUpgrade => UpgradeId != 0;
}
