using System.Collections.ObjectModel;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 选中单位独立选项卡的 ViewModel。
/// 包含 4 个分组（伤害与无敌、选中单位·生命值、选中单位·速度、选中单位·其他）的 FeatureItemViewModel
/// 以及一个「单位升级」子面板。
/// 布局沿用 FeaturesPage 的 ActionCard 网格，但不含搜索框和资源值输入框。
/// </summary>
public sealed class SelectedUnitViewModel : ViewModelBase
{
    private readonly IFeatureHost _host;
    private readonly ObservableCollection<FeatureGroupViewModel> _groups;
    private readonly Func<ITrainerFeatureController?> _getController;
    private const string SetTargetHealthRawName = TrainerFeatureIds.SetSelectedUnitTargetHealth;
    private string _selectedUnitTargetHealthText = string.Empty;
    private string _selectedUnitTargetMaxHealthText = string.Empty;
    private string _attackSpeedMultiplierText = string.Empty;
    private string _attackRangeMultiplierText = string.Empty;
    private string _damageMultiplierText = string.Empty;
    private string _maxHealthMultiplierText = string.Empty;
    private string _moveSpeedMultiplierText = string.Empty;

    public SelectedUnitViewModel(
        IFeatureHost host,
        IReadOnlyList<TrainerFeature> features,
        Func<ITrainerFeatureController?> getController,
        Func<TrainerFeature, FeatureCapabilitySnapshot> getCapability)
    {
        _host = host;
        _getController = getController;
        _groups = new(CreateGroups(features));
        UnitUpgrade = new UnitUpgradeViewModel(getController, getCapability);
    }

    public UnitUpgradeViewModel UnitUpgrade { get; }

    public string SelectedUnitTargetHealthText
    {
        get => _selectedUnitTargetHealthText;
        set { _selectedUnitTargetHealthText = value; OnPropertyChanged(); }
    }

    public string SelectedUnitTargetMaxHealthText
    {
        get => _selectedUnitTargetMaxHealthText;
        set { _selectedUnitTargetMaxHealthText = value; OnPropertyChanged(); }
    }

    /// <summary>攻速倍率输入（2–128 任意整数，二进制位权组合）。</summary>
    public string AttackSpeedMultiplierText
    {
        get => _attackSpeedMultiplierText;
        set { _attackSpeedMultiplierText = value; OnPropertyChanged(); }
    }

    /// <summary>射程倍率输入（2–128 任意整数，二进制位权组合）。</summary>
    public string AttackRangeMultiplierText
    {
        get => _attackRangeMultiplierText;
        set { _attackRangeMultiplierText = value; OnPropertyChanged(); }
    }

    /// <summary>伤害倍率输入（2–128 任意整数，二进制位权组合）。</summary>
    public string DamageMultiplierText
    {
        get => _damageMultiplierText;
        set { _damageMultiplierText = value; OnPropertyChanged(); }
    }

    /// <summary>生命倍率输入（2–128 任意整数，二进制位权组合）。</summary>
    public string MaxHealthMultiplierText
    {
        get => _maxHealthMultiplierText;
        set { _maxHealthMultiplierText = value; OnPropertyChanged(); }
    }

    /// <summary>移速倍率输入（乘法因子，仅支持 2 的幂：2/4/8/16/32/64/128）。</summary>
    public string MoveSpeedMultiplierText
    {
        get => _moveSpeedMultiplierText;
        set { _moveSpeedMultiplierText = value; OnPropertyChanged(); }
    }

    /// <summary>按功能 RawName 读取对应的倍率输入文本；非倍率功能返回空串。</summary>
    public string GetMultiplierText(string featureRawName) => featureRawName switch
    {
        TrainerFeatureIds.ProductAttackSpeedCustom => AttackSpeedMultiplierText,
        TrainerFeatureIds.ProductAttackRangeCustom => AttackRangeMultiplierText,
        TrainerFeatureIds.ProductAttackDamageCustom => DamageMultiplierText,
        TrainerFeatureIds.ProductMaxHealthCustom => MaxHealthMultiplierText,
        TrainerFeatureIds.ProductMoveSpeedCustom => MoveSpeedMultiplierText,
        _ => string.Empty,
    };

    /// <summary>供参数持久化 provider 捕获五个独立倍率输入。</summary>
    public IReadOnlyDictionary<string, string> CaptureMultiplierTexts() => new Dictionary<string, string>
    {
        ["selectedUnit.multiplier.attackSpeed"] = AttackSpeedMultiplierText,
        ["selectedUnit.multiplier.attackRange"] = AttackRangeMultiplierText,
        ["selectedUnit.multiplier.attackDamage"] = DamageMultiplierText,
        ["selectedUnit.multiplier.maxHealth"] = MaxHealthMultiplierText,
        ["selectedUnit.multiplier.moveSpeed"] = MoveSpeedMultiplierText,
    };

    /// <summary>供参数持久化 provider 恢复倍率输入（仅写回存在的 key）。</summary>
    public void RestoreMultiplierTexts(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("selectedUnit.multiplier.attackSpeed", out var attackSpeed))
            AttackSpeedMultiplierText = attackSpeed;
        if (values.TryGetValue("selectedUnit.multiplier.attackRange", out var attackRange))
            AttackRangeMultiplierText = attackRange;
        if (values.TryGetValue("selectedUnit.multiplier.attackDamage", out var damage))
            DamageMultiplierText = damage;
        if (values.TryGetValue("selectedUnit.multiplier.maxHealth", out var maxHealth))
            MaxHealthMultiplierText = maxHealth;
        if (values.TryGetValue("selectedUnit.multiplier.moveSpeed", out var moveSpeed))
            MoveSpeedMultiplierText = moveSpeed;
    }

    public string SelectedUnitTargetHealthHelpText =>
        "设置选中建筑物/单位的当前生命值为指定浮点数值；不修改最大生命值上限。";

    public string SelectedUnitMaxHealthHelpText =>
        "无限生命值开启后写入的最大生命值；默认 9999999。";

    public string AdditiveMultiplierHelpText =>
        "倍率输入框：支持 2–128 的任意整数；内部按二进制位权自动组合（例如 7 倍 = 4+2+1 三个模板叠加）。超过 16 倍的攻速需要射速钳制能力就绪。";

    public string MoveSpeedMultiplierHelpText =>
        "移速倍率输入框：移速是乘法因子，仅支持 2 的幂（2/4/8/16/32/64/128）；其他整数无法用乘法模板组合。";

    public ObservableCollection<FeatureGroupViewModel> Groups => _groups;

    public IEnumerable<FeatureItemViewModel> AllFeatureItems() =>
        _groups.SelectMany(g => g.Features);

    public void RefreshToggleStates()
    {
        foreach (var item in AllFeatureItems())
        {
            if (item.IsToggle)
                item.RefreshToggleState();
        }
    }

    public void ResetToggleStates()
    {
        foreach (var item in AllFeatureItems())
        {
            if (item.IsToggle)
                item.ResetToggleState();
        }
    }

    public void RaiseFeatureCommandStates()
    {
        foreach (var item in AllFeatureItems())
            item.RaiseCommandState();
        UnitUpgrade.RaiseCommands();
    }

    public void WriteTargetHealthIfNeeded(TrainerFeature feature)
    {
        if (!feature.RawName.Equals(SetTargetHealthRawName, StringComparison.Ordinal) ||
            _host.FeatureController is null)
        {
            return;
        }

        var text = _selectedUnitTargetHealthText.Trim();
        if (!float.TryParse(text, out var targetHealth) || targetHealth <= 0)
        {
            return;
        }

        var maxHealthText = _selectedUnitTargetMaxHealthText.Trim();
        var targetMaxHealth = float.TryParse(maxHealthText, out var maxHealth) && maxHealth > 0
            ? maxHealth
            : 0f;
        _host.FeatureController.WriteTargetHealthValue(targetHealth, targetMaxHealth);
    }

    private IReadOnlyList<FeatureGroupViewModel> CreateGroups(IReadOnlyList<TrainerFeature> features)
    {
        var selectedUnitNames = TrainerFeatureGroupCatalog.SelectedUnitGroupingNames;
        var selectedFeatures = features
            .Where(f => selectedUnitNames.Contains(f.DisplayName, StringComparer.Ordinal))
            .ToDictionary(f => f.DisplayName);

        var groups = new List<FeatureGroupViewModel>();

        AddGroup(groups, GroupIds.SelectedUnitDamage, "伤害与无敌",
        [
            "玩家全建筑/单位无敌",
            "一击必杀敌方建筑物/单位",
        ], selectedFeatures, isExpanded: true);

        AddGroup(groups, GroupIds.SelectedUnitHealth, "选中单位 · 生命值",
        [
            "选择的建筑物/单位无限生命值",
            "选择的建筑物/单位生命值变为1",
            "选择的建筑物/单位恢复原本的生命值",
            "设置选中建筑物/单位生命值为指定值",
        ], selectedFeatures, isExpanded: true);

        AddGroup(groups, GroupIds.SelectedUnitSpeed, "选中单位 · 速度",
        [
            "选择的单位高速移动",
            "选择的单位缓慢移动",
            "选择的单位暂停",
            "选择的单位恢复速度",
            "选择的单位移速（自定义倍率）",
        ], selectedFeatures, isExpanded: true);
        
        AddGroup(groups, GroupIds.SelectedUnitOther, "选中单位 · 其他",
        [
            "选择的单位快速升级",
            "移动选中单位到鼠标位置",
            "选择的单位满攻速（无限射速）",
            "选择的单位满攻速（停用）",
            "选择的单位攻速（自定义倍率）",
            "选择的单位无限射程与索敌（启用）",
            "选择的单位无限射程与索敌（停用）",
            "选择的单位射程（自定义倍率）",
            "选择的单位伤害（自定义倍率）",
            "选择的单位生命（自定义倍率）",
            "清除所有单位的满攻速效果",
            "清除所有单位的无限射程效果",
            "选择的单位弹药填满",
            "选择的单位弹药归1",
            "俘虏选择的建筑物/单位",
            "摧毁选择的建筑物/单位",
            "选择的建筑物/单位设置伪装状态",
        ], selectedFeatures, isExpanded: true);

        return groups;
    }

    private void AddGroup(
        List<FeatureGroupViewModel> groups,
        string groupId,
        string groupName,
        string[] displayNames,
        Dictionary<string, TrainerFeature> selectedFeatures,
        bool isExpanded)
    {
        var items = new List<FeatureItemViewModel>();
        foreach (var displayName in displayNames)
        {
            if (selectedFeatures.TryGetValue(displayName, out var feature))
            {
                items.Add(new FeatureItemViewModel(feature, _host));
            }
        }
        if (items.Count > 0)
            groups.Add(new FeatureGroupViewModel(groupId, groupName, items, isExpanded));
    }
}
