using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 功能开关域：Groups（功能分组 + 折叠/搜索）、资源值输入框、资源值/目标生命值写入。
/// FeaturesPage 全部内容归本 VM。分组折叠通过 FeatureGroupViewModel.IsExpanded + ActionCard 原生支持。
/// </summary>
public sealed class FeatureToggleViewModel : ViewModelBase
{
    private const string SetTargetHealthRawName = TrainerFeatureIds.SetSelectedUnitTargetHealth;

    private readonly IFeatureHost _host;
    private readonly ObservableCollection<FeatureGroupViewModel> _groups;
    private string _searchText = string.Empty;
    private string _moneyAmountText;
    private string _powerValueText;
    private string _scPointValueText;
    private string _selectedUnitTargetHealthText;
    private string _selectedUnitTargetMaxHealthText;

    public FeatureToggleViewModel(
        IFeatureHost host,
        IEnumerable<TrainerFeature> configuredFeatures,
        TrainerAppSettings settings)
    {
        _host = host;
        _moneyAmountText = settings.ResourceValues.MoneyAmount.ToString();
        _powerValueText = settings.ResourceValues.PowerValue.ToString();
        _scPointValueText = settings.ResourceValues.ScPointValue.ToString();
        _selectedUnitTargetHealthText = string.Empty;
        _selectedUnitTargetMaxHealthText = string.Empty;
        _groups = new ObservableCollection<FeatureGroupViewModel>(CreateGroups(configuredFeatures));
        FilteredGroups = CollectionViewSource.GetDefaultView(_groups);
        FilteredGroups.Filter = FilterGroup;
    }

    /// <summary>原始分组数据，供 AllFeatureItems / 测试访问。</summary>
    public ObservableCollection<FeatureGroupViewModel> Groups => _groups;

    /// <summary>过滤后的分组视图，FeaturesPage.xaml ItemsSource 绑定此属性。</summary>
    public ICollectionView FilteredGroups { get; }

    public string MoneyAmountText { get => _moneyAmountText; set { _moneyAmountText = value; OnPropertyChanged(); } }
    public string PowerValueText { get => _powerValueText; set { _powerValueText = value; OnPropertyChanged(); } }
    public string ScPointValueText { get => _scPointValueText; set { _scPointValueText = value; OnPropertyChanged(); } }
    public string SelectedUnitTargetHealthText { get => _selectedUnitTargetHealthText; set { _selectedUnitTargetHealthText = value; OnPropertyChanged(); } }
    public string SelectedUnitTargetMaxHealthText { get => _selectedUnitTargetMaxHealthText; set { _selectedUnitTargetMaxHealthText = value; OnPropertyChanged(); } }

    public string MoneyAmountHelpText => "资金功能每次执行时增加的金额；默认 100000。";
    public string PowerValueHelpText => "无限电力开启后写入的可用电力值；默认 100000。";
    public string ScPointValueHelpText => "秘密协议点数功能写入的点数值，范围 0-15。";
    public string SelectedUnitMaxHealthHelpText => "无限生命值开启后写入的最大生命值；默认 9999999。";
    public string SelectedUnitTargetHealthHelpText => "设置选中建筑物/单位的当前生命值为指定浮点数值；不修改最大生命值上限。";

    /// <summary>搜索过滤文本。变化时自动刷新 Groups 视图。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            FilteredGroups.Refresh();
        }
    }

    public ResourceValueSettings GetResourceValueSettings() =>
        ResourceValueSettings.Parse(MoneyAmountText, PowerValueText, ScPointValueText);

    public void WriteResourceValuesIfNeeded(TrainerFeature feature)
    {
        if (IsResourceValueFeature(feature) && _host.FeatureController is not null)
        {
            _host.FeatureController.WriteResourceValues(GetResourceValueSettings());
        }
    }

    public void WriteTargetHealthIfNeeded(TrainerFeature feature)
    {
        if (!feature.RawName.Equals(SetTargetHealthRawName, StringComparison.Ordinal) || _host.FeatureController is null)
        {
            return;
        }

        var text = _selectedUnitTargetHealthText.Trim();
        if (!float.TryParse(text, out var targetHealth) || targetHealth <= 0)
        {
            return;
        }

        var maxHealthText = _selectedUnitTargetMaxHealthText.Trim();
        var targetMaxHealth = float.TryParse(maxHealthText, out var maxHealth) && maxHealth > 0 ? maxHealth : 0f;
        _host.FeatureController.WriteTargetHealthValue(targetHealth, targetMaxHealth);
    }

    /// <summary>返回本 VM 持有的所有 FeatureItemViewModel（Groups 内的 Features）。供 AllFeatureItems 跨 VM 聚合。</summary>
    public IEnumerable<FeatureItemViewModel> AllFeatureItems() =>
        _groups.SelectMany(g => g.Features);

    /// <summary>刷新所有 toggle 功能的开关状态。供 RefreshFeatureStates 委托。</summary>
    public void RefreshToggleStates()
    {
        foreach (var item in AllFeatureItems())
        {
            if (item.IsToggle)
            {
                item.RefreshToggleState();
            }
        }
    }

    /// <summary>断开/恢复 patch 时把所有 toggle 功能状态重置为"未启用"，避免显示陈旧的已启用状态。</summary>
    public void ResetToggleStates()
    {
        foreach (var item in AllFeatureItems())
        {
            if (item.IsToggle)
            {
                item.ResetToggleState();
            }
        }
    }

    /// <summary>刷新所有功能命令的 CanExecute。供 MainVM.RaiseCommandStates() 遍历。</summary>
    public void RaiseFeatureCommandStates()
    {
        foreach (var item in AllFeatureItems())
        {
            item.RaiseCommandState();
        }
    }

    public void RaiseCommandStates()
    {
        // FeatureToggleVM 没有自己的 RelayCommand，但保留供 MainVM 统一调用
    }

    private bool FilterGroup(object item)
    {
        if (item is not FeatureGroupViewModel group)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        return group.Features.Any(f => f.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<FeatureGroupViewModel> CreateGroups(IEnumerable<TrainerFeature> features)
    {
        var items = features.Select(feature => new FeatureItemViewModel(feature, _host)).ToArray();
        return TrainerFeatureGroupCatalog.Groups.Select(g =>
            new FeatureGroupViewModel(g.Name, SelectItems(items, g.FeatureDisplayNames), g.IsExpanded)
        ).ToArray();
    }

    private static IReadOnlyList<FeatureItemViewModel> SelectItems(
        IReadOnlyList<FeatureItemViewModel> items, IReadOnlyList<string> displayNames)
    {
        return displayNames
            .Select(name => items.FirstOrDefault(item => item.DisplayName == name))
            .Where(item => item is not null)
            .Cast<FeatureItemViewModel>()
            .ToArray();
    }

    private static bool IsResourceValueFeature(TrainerFeature feature) =>
        feature.RawName is "Moeny" or "Power" or "SC POINT";

}
