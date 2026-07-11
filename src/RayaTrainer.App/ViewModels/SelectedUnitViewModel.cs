using System.Collections.ObjectModel;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 选中单位独立选项卡的 ViewModel。
/// 只包含 4 个分组（伤害与无敌、选中单位·生命值、选中单位·速度、选中单位·其他）的 FeatureItemViewModel。
/// 布局沿用 FeaturesPage 的 ActionCard 网格，但不含搜索框和资源值输入框。
/// </summary>
public sealed class SelectedUnitViewModel : ViewModelBase
{
    private readonly IFeatureHost _host;
    private readonly ObservableCollection<FeatureGroupViewModel> _groups;

    public SelectedUnitViewModel(IFeatureHost host, IReadOnlyList<TrainerFeature> features)
    {
        _host = host;
        _groups = new(CreateGroups(features));
    }

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
    }

    private IReadOnlyList<FeatureGroupViewModel> CreateGroups(IReadOnlyList<TrainerFeature> features)
    {
        var selectedUnitNames = TrainerFeatureGroupCatalog.SelectedUnitGroupingNames;
        var selectedFeatures = features
            .Where(f => selectedUnitNames.Contains(f.DisplayName, StringComparer.Ordinal))
            .ToDictionary(f => f.DisplayName);

        var groups = new List<FeatureGroupViewModel>();

        AddGroup(groups, "伤害与无敌",
        [
            "玩家全建筑/单位无敌",
            "一击必杀敌方建筑物/单位",
        ], selectedFeatures, isExpanded: true);

        AddGroup(groups, "选中单位 · 生命值",
        [
            "选择的建筑物/单位无限生命值",
            "选择的建筑物/单位生命值变为1",
            "选择的建筑物/单位恢复原本的生命值",
            "设置选中建筑物/单位生命值为指定值",
        ], selectedFeatures, isExpanded: true);

        AddGroup(groups, "选中单位 · 速度",
        [
            "选择的单位高速移动",
            "选择的单位缓慢移动",
            "选择的单位暂停",
            "选择的单位恢复速度",
        ], selectedFeatures, isExpanded: true);

        AddGroup(groups, "选中单位 · 其他",
        [
            "选择的单位快速升级",
            "移动选中单位到鼠标位置",
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
            groups.Add(new FeatureGroupViewModel(groupName, items, isExpanded));
    }
}
