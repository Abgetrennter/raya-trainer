using RayaTrainer.App.ViewModels;
using RayaTrainer.App.ViewModels.FeatureParameterProviders;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class FeatureParameterProviderTests
{
    [Fact]
    public void ResourceProvider_CaptureValidated_ReturnsInvariantIntegers()
    {
        var provider = new ResourceParameterProvider(
            () => new ResourceValueSettings(5000, 200, 10));

        var captured = provider.CaptureValidated();

        Assert.Equal("5000", captured["resources.moneyAmount"]);
        Assert.Equal("200", captured["resources.powerValue"]);
        Assert.Equal("10", captured["resources.scPointValue"]);
    }

    [Fact]
    public void ResourceProvider_CaptureValidated_HalfInput_KeepsLastValid()
    {
        // 模拟用户正在输入空串/非法 → provider 应返回最后有效值
        var provider = new ResourceParameterProvider(
            () => throw new FormatException(),
            lastValid: new ResourceValueSettings(100, 100, 5));

        var captured = provider.CaptureValidated();

        Assert.Equal("100", captured["resources.moneyAmount"]);
    }

    [Fact]
    public void ResourceProvider_RestoreValidated_SuppressRuntimeApply_StillWritesBackUiText()
    {
        // writeBack is UI-only (sets text fields), must always run even when suppressRuntimeApply=true.
        ResourceValueSettings? written = null;
        var provider = new ResourceParameterProvider(
            () => ResourceValueSettings.Default,
            writeBack: v => written = v);

        provider.RestoreValidated(
            new Dictionary<string, string>
            {
                ["resources.moneyAmount"] = "9999",
                ["resources.powerValue"] = "8888",
                ["resources.scPointValue"] = "3"
            },
            suppressRuntimeApply: true);

        Assert.NotNull(written);
        Assert.Equal(9999, written!.MoneyAmount);
        Assert.Equal(8888, written.PowerValue);
        Assert.Equal(3, written.ScPointValue);
    }

    [Fact]
    public void ResourceProvider_RestoreValidated_Valid_WritesBack()
    {
        ResourceValueSettings? written = null;
        var provider = new ResourceParameterProvider(
            () => ResourceValueSettings.Default,
            writeBack: v => written = v);

        provider.RestoreValidated(
            new Dictionary<string, string>
            {
                ["resources.moneyAmount"] = "9999",
                ["resources.powerValue"] = "8888",
                ["resources.scPointValue"] = "3"
            },
            suppressRuntimeApply: false);

        Assert.NotNull(written);
        Assert.Equal(9999, written!.MoneyAmount);
        Assert.Equal(8888, written.PowerValue);
        Assert.Equal(3, written.ScPointValue);
    }

    [Fact]
    public void ResourceProvider_RestoreValidated_OutOfRange_SkipsItem_ReportsError()
    {
        ResourceValueSettings? written = null;
        var provider = new ResourceParameterProvider(
            () => ResourceValueSettings.Default,
            writeBack: v => written = v);

        var result = provider.RestoreValidated(
            new Dictionary<string, string> { ["resources.scPointValue"] = "999" },
            suppressRuntimeApply: false);

        // 越界跳过该项；不报成功
        Assert.False(result.AppliedIds.Contains("resources.scPointValue"));
        Assert.Contains("resources.scPointValue", result.SkippedIds);
    }

    [Fact]
    public void SelectedUnitProvider_CapturesTargetHealth_InvariantFloat()
    {
        var provider = new SelectedUnitParameterProvider(
            () => ("5000", "10000"),
            (h, m) => { });

        var captured = provider.CaptureValidated();
        Assert.Equal("5000", captured["selectedUnit.targetHealth.current"]);
        Assert.Equal("10000", captured["selectedUnit.targetHealth.max"]);
    }

    [Fact]
    public void SelectedUnitProvider_CaptureEmptyText_OmitsKey()
    {
        var provider = new SelectedUnitParameterProvider(
            () => ("", ""),
            (h, m) => { });

        var captured = provider.CaptureValidated();
        // 空串不视为有效值，省略 key（不覆盖默认）
        Assert.DoesNotContain("selectedUnit.targetHealth.current", captured.Keys);
    }

    [Fact]
    public void SelectedUnitProvider_RestoreValidated_WritesBackText()
    {
        string? health = null, max = null;
        var provider = new SelectedUnitParameterProvider(
            () => ("", ""),
            (h, m) => { health = h; max = m; });

        provider.RestoreValidated(
            new Dictionary<string, string>
            {
                ["selectedUnit.targetHealth.current"] = "7500",
                ["selectedUnit.targetHealth.max"] = "9000"
            },
            suppressRuntimeApply: false);

        Assert.Equal("7500", health);
        Assert.Equal("9000", max);
    }

    [Fact]
    public void TemplateReplacementProvider_CapturesAndRestores()
    {
        string? target = null, donor = null;
        var provider = new TemplateReplacementParameterProvider(
            () => ("0x1234", "0x5678"),
            (t, d) => { target = t; donor = d; });

        var captured = provider.CaptureValidated();
        Assert.Equal("0x1234", captured["templateReplacement.targetUnitId"]);
        Assert.Equal("0x5678", captured["templateReplacement.donorUnitId"]);

        provider.RestoreValidated(
            new Dictionary<string, string>
            {
                ["templateReplacement.targetUnitId"] = "0xABCD",
                ["templateReplacement.donorUnitId"] = "0x9999"
            },
            suppressRuntimeApply: false);

        Assert.Equal("0xABCD", target);
        Assert.Equal("0x9999", donor);
    }

    [Fact]
    public void ResourceProvider_RestoreValidated_PartialPreset_KeepsCurrentNotStale()
    {
        // 当前值是 (9999, 8888, 3)
        var current = new ResourceValueSettings(9999, 8888, 3);
        ResourceValueSettings? written = null;
        var provider = new ResourceParameterProvider(
            capture: () => current,
            writeBack: v => written = v,
            lastValid: new ResourceValueSettings(1, 1, 0)); // 陈旧 fallback

        // 预设只提供 powerValue
        provider.RestoreValidated(
            new Dictionary<string, string> { ["resources.powerValue"] = "500" },
            suppressRuntimeApply: false);

        Assert.NotNull(written);
        Assert.Equal(9999, written!.MoneyAmount);  // 保留当前，不用陈旧 1
        Assert.Equal(500, written.PowerValue);      // 预设值
        Assert.Equal(3, written.ScPointValue);      // 保留当前，不用陈旧 0
    }
}
