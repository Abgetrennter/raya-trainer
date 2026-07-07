using System.Collections.ObjectModel;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels;

public sealed class StatusBitEditorPanelViewModel : ViewModelBase
{
    private readonly IReadOnlyList<StatusBitDefinition> _allStatuses;
    private readonly Func<StatusBitDefinition, bool, Task<GameApiDispatchStatus>> _writer;
    private readonly Func<bool> _canExecute;
    private readonly Action<string> _setStatusMessage;
    private string _searchText = string.Empty;
    private bool _showAllStatusFields;
    private StatusBitDomainOption _selectedDomain;
    private StatusBitCategoryOption _selectedCategory;

    public StatusBitEditorPanelViewModel(
        IEnumerable<StatusBitDefinition> statuses,
        Func<StatusBitDefinition, bool, Task<GameApiDispatchStatus>> writer,
        Func<bool> canExecute,
        Action<string> setStatusMessage)
    {
        _allStatuses = statuses.ToArray();
        _writer = writer;
        _canExecute = canExecute;
        _setStatusMessage = setStatusMessage;
        FilteredStatuses = new ObservableCollection<StatusBitRowViewModel>();
        DomainOptions = new ObservableCollection<StatusBitDomainOption>(
        [
            StatusBitDomainOption.All,
            new("ObjectStatus", StatusBitDomain.ObjectStatus),
            new("ModelConditionFlags", StatusBitDomain.ModelConditionFlags)
        ]);
        CategoryOptions = new ObservableCollection<StatusBitCategoryOption>(
            [StatusBitCategoryOption.All, .. _allStatuses
                .Select(item => item.Category)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(category => new StatusBitCategoryOption(category, category))]);
        _selectedDomain = DomainOptions[0];
        _selectedCategory = CategoryOptions[0];
        RefreshFilteredStatuses();
    }

    public ObservableCollection<StatusBitRowViewModel> FilteredStatuses { get; }

    public ObservableCollection<StatusBitDomainOption> DomainOptions { get; }

    public ObservableCollection<StatusBitCategoryOption> CategoryOptions { get; }

    public StatusBitDomainOption SelectedDomain
    {
        get => _selectedDomain;
        set
        {
            if (_selectedDomain == value)
            {
                return;
            }

            _selectedDomain = value;
            OnPropertyChanged();
            RefreshFilteredStatuses();
        }
    }

    public StatusBitCategoryOption SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value)
            {
                return;
            }

            _selectedCategory = value;
            OnPropertyChanged();
            RefreshFilteredStatuses();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText.Equals(value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            RefreshFilteredStatuses();
        }
    }

    public bool ShowAllStatusFields
    {
        get => _showAllStatusFields;
        set
        {
            if (_showAllStatusFields == value)
            {
                return;
            }

            _showAllStatusFields = value;
            OnPropertyChanged();
            RefreshFilteredStatuses();
        }
    }

    public string HelpText => "对当前选中单位执行一次性 ObjectStatus / ModelConditionFlags 位写入；这里不读取真实当前状态。";

    public string AllStatusFieldsHelpText => "默认只显示第一批适合做修改器功能的状态位；勾选后显示全部 ObjectStatus / ModelConditionFlags 字段。";

    public string AvailabilityText => _canExecute()
        ? "DLL Agent 状态位写入可用。"
        : "状态位编辑器需要 DLL Agent 后端并已安装 patch。";

    public async Task ApplyAsync(StatusBitRowViewModel row, bool enabled)
    {
        if (!_canExecute())
        {
            row.LastResult = "不可用";
            _setStatusMessage("状态位编辑器需要 DLL Agent 后端并已安装 patch。");
            return;
        }

        try
        {
            var dispatchStatus = await _writer(row.Definition, enabled);
            row.LastResult = dispatchStatus.ToString();
            row.LastAction = enabled ? "置 1" : "清 0";
            _setStatusMessage(
                $"状态位写入 {dispatchStatus}: {row.Definition.Name} ({FormatDomain(row.Definition.Domain)} bit {row.Definition.BitIndex}) -> {(enabled ? 1 : 0)}。");
        }
        catch (Exception ex)
        {
            row.LastResult = "失败";
            _setStatusMessage($"状态位写入失败：{ex.Message}");
        }
    }

    public void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(AvailabilityText));
        foreach (var row in FilteredStatuses)
        {
            row.RaiseCommandStates();
        }
    }

    private void RefreshFilteredStatuses()
    {
        FilteredStatuses.Clear();
        foreach (var definition in _allStatuses.Where(MatchesFilter)
                     .OrderBy(item => item.Domain)
                     .ThenBy(item => item.BitIndex))
        {
            StatusBitRowViewModel? row = null;
            row = new StatusBitRowViewModel(
                definition,
                () => _ = ApplyAsync(row!, enabled: true),
                () => _ = ApplyAsync(row!, enabled: false),
                _canExecute);
            FilteredStatuses.Add(row);
        }
    }

    private bool MatchesFilter(StatusBitDefinition definition)
    {
        if (!_showAllStatusFields && !definition.IsRecommendedFunction)
        {
            return false;
        }

        if (_selectedDomain.Domain is StatusBitDomain selectedDomain && definition.Domain != selectedDomain)
        {
            return false;
        }

        if (_selectedCategory.Category is string selectedCategory &&
            !definition.Category.Equals(selectedCategory, StringComparison.Ordinal))
        {
            return false;
        }

        var search = _searchText.Trim();
        return search.Length == 0 ||
            definition.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            definition.BitIndex.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            FormatDomain(definition.Domain).Contains(search, StringComparison.OrdinalIgnoreCase) ||
            definition.Category.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDomain(StatusBitDomain domain)
    {
        return domain == StatusBitDomain.ObjectStatus ? "ObjectStatus" : "ModelConditionFlags";
    }
}

public sealed class StatusBitRowViewModel : ViewModelBase
{
    private string _lastAction = "";
    private string _lastResult = "";

    public StatusBitRowViewModel(
        StatusBitDefinition definition,
        Action set,
        Action clear,
        Func<bool> canExecute)
    {
        Definition = definition;
        SetCommand = new RelayCommand(set, canExecute);
        ClearCommand = new RelayCommand(clear, canExecute);
    }

    public StatusBitDefinition Definition { get; }

    public RelayCommand SetCommand { get; }

    public RelayCommand ClearCommand { get; }

    public string DomainText => Definition.Domain == StatusBitDomain.ObjectStatus
        ? "ObjectStatus"
        : "ModelCondition";

    public string BitText => Definition.BitIndex.ToString();

    public string RiskText => Definition.RiskLevel switch
    {
        StatusBitRiskLevel.Dangerous => "危险",
        StatusBitRiskLevel.Volatile => "易变",
        _ => "普通"
    };

    public string HelpText => CreateCompactHelpText(Definition.HelpText);

    public string LastAction
    {
        get => _lastAction;
        set
        {
            _lastAction = value;
            OnPropertyChanged();
        }
    }

    public string LastResult
    {
        get => _lastResult;
        set
        {
            _lastResult = value;
            OnPropertyChanged();
        }
    }

    public void RaiseCommandStates()
    {
        SetCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
    }

    private static string CreateCompactHelpText(string source)
    {
        var text = source;
        var referenceIndex = text.IndexOf("参考：", StringComparison.Ordinal);
        if (referenceIndex >= 0)
        {
            text = text[referenceIndex..];
        }
        else
        {
            var firstSentenceEnd = text.IndexOf('。');
            if (firstSentenceEnd >= 0 && firstSentenceEnd + 1 < text.Length)
            {
                text = text[(firstSentenceEnd + 1)..];
            }
        }

        foreach (var suffix in RepeatedHelpSuffixes)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                text = text[..^suffix.Length];
                break;
            }
        }

        text = text.Trim();
        return text.Length == 0 ? "参考资料未给出明确作用。" : text;
    }

    private static readonly string[] RepeatedHelpSuffixes =
    [
        "一次性写入当前选中单位；本面板不读取真实当前状态。",
        "易被引擎更新逻辑覆盖；本面板只做一次性写入。",
        "危险状态，可能导致单位消失、死亡、不可选或进入特殊终止态。"
    ];
}

public sealed record StatusBitDomainOption(string DisplayName, StatusBitDomain? Domain)
{
    public static StatusBitDomainOption All { get; } = new("全部状态域", null);
}

public sealed record StatusBitCategoryOption(string DisplayName, string? Category)
{
    public static StatusBitCategoryOption All { get; } = new("全部分类", null);
}
