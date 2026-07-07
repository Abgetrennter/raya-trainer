using RayaTrainer.Core.Hashing;
using Xunit;

namespace RayaTrainer.Tests;

/// <summary>
/// Ra3InstanceIdHash 回归测试。
///
/// 这个哈希函数是协议授予的命脉：SecretProtocolCatalog 把 PlayerTech_/Upgrade_ 名称经
/// Ra3InstanceIdHash.Compute 转成 32-bit ID，写入 iEnable+5C/+60，bootstrap 090/110 段
/// 把它 push 给 Science_FindProtocolItemById (0x43C300) 在引擎 ScienceStore 红黑树里
/// 精确查找。算错一个位都会导致查找 miss、授予静默失败。
///
/// 下面的期望值是经过三重确认的引擎真值（2026-07-01）：
///   1. 与 bootstrap probe/grant 里硬编码的常量一致（080/090/110 源码）
///   2. 运行时 attach RA3 遍历 ScienceStore 命名空间 0x7F0818D9 的红黑树，确认这些 key 存在
///   3. 与 Corona MOD MapCoreLib 的 fash hash 实现一致
/// 任何人想优化/替换此算法，必须先保证这些对子仍然命中。
/// </summary>
public sealed class Ra3InstanceIdHashTests
{
    [Theory]
    [InlineData("PlayerTech_Allied_AirPower", 0xDD6C4C5B)]
    [InlineData("Upgrade_AlliedAirPower", 0x33D87C97)]
    [InlineData("PlayerTech_Japan_EnhancedKamikaze", 0xFBE46678)]
    [InlineData("Upgrade_JapanEnhancedKamikaze", 0x5F7C162F)]
    [InlineData("PlayerTech_Soviet_OrbitalRefuse_Rank1", 0x3A7E2F69)]
    public void ComputeMatchesEngineScienceStoreKeys(string content, uint expected)
    {
        // 这些期望值是引擎 ScienceStore 运行时实测的真 key（见类注释的三重确认）。
        // 命中 ScienceStore 是协议授予生效的前提；改算法会破坏全部协议授予。
        Assert.Equal(expected, Ra3InstanceIdHash.Compute(content));
    }

    [Fact]
    public void ComputeUsesCaseInsensitiveAsciiInput()
    {
        Assert.Equal(
            Ra3InstanceIdHash.Compute("PlayerTech_Allied_AirPower"),
            Ra3InstanceIdHash.Compute("playertech_allied_airpower"));
    }
}
