using System.Collections.ObjectModel;
using System.Windows.Input;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 功能预设面板 VM。挂在 FeaturesPage 顶部。
/// 暴露：预设名列表、新建/覆盖、装载、重命名、删除、部分应用摘要。
/// </summary>
public sealed class FeaturePresetViewModel : ViewModelBase
{
    private readonly MainViewModel _owner;
    private string _newPresetName = string.Empty;
    private string? _selectedPresetName;
    private string _lastAppliedSummary = string.Empty;

    public FeaturePresetViewModel(MainViewModel owner)
    {
        _owner = owner;
        SaveCommand = new RelayCommand(SavePreset, () => !string.IsNullOrWhiteSpace(NewPresetName));
        ApplyCommand = new RelayCommand(ApplyPreset, () => !string.IsNullOrWhiteSpace(SelectedPresetName));
        DeleteCommand = new RelayCommand(DeletePreset, () => !string.IsNullOrWhiteSpace(SelectedPresetName));
        RenameCommand = new RelayCommand(RenamePreset, () => !string.IsNullOrWhiteSpace(SelectedPresetName) && !string.IsNullOrWhiteSpace(NewPresetName));
    }

    public ObservableCollection<string> PresetNames { get; } = new();

    public string NewPresetName
    {
        get => _newPresetName;
        set { _newPresetName = value; OnPropertyChanged(); SaveCommand.RaiseCanExecuteChanged(); RenameCommand.RaiseCanExecuteChanged(); }
    }

    public string? SelectedPresetName
    {
        get => _selectedPresetName;
        set { _selectedPresetName = value; OnPropertyChanged(); ApplyCommand.RaiseCanExecuteChanged(); DeleteCommand.RaiseCanExecuteChanged(); RenameCommand.RaiseCanExecuteChanged(); }
    }

    public string LastAppliedSummary
    {
        get => _lastAppliedSummary;
        private set { _lastAppliedSummary = value; OnPropertyChanged(); }
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RenameCommand { get; }

    public void RefreshPresetNames()
    {
        PresetNames.Clear();
        foreach (var p in _owner.FeaturePresets)
            PresetNames.Add(p.Name);
    }

    private void SavePreset()
    {
        _owner.SaveFeaturePreset(NewPresetName.Trim());
        RefreshPresetNames();
        SelectedPresetName = NewPresetName.Trim();
        NewPresetName = string.Empty;
    }

    private void ApplyPreset()
    {
        if (SelectedPresetName is null) return;
        var result = _owner.ApplyFeaturePreset(SelectedPresetName);
        LastAppliedSummary = result.AppliedToggles.Count == 0 && result.SkippedToggles.Count == 0
            ? $"已装载「{SelectedPresetName}」（无 toggle 变更）"
            : $"已装载「{SelectedPresetName}」：应用 {result.AppliedToggles.Count}，跳过 {result.SkippedToggles.Count}（不可用）";
    }

    private void DeletePreset()
    {
        if (SelectedPresetName is null) return;
        _owner.DeleteFeaturePreset(SelectedPresetName);
        RefreshPresetNames();
        SelectedPresetName = PresetNames.FirstOrDefault();
    }

    private void RenamePreset()
    {
        if (SelectedPresetName is null) return;
        if (_owner.RenameFeaturePreset(SelectedPresetName, NewPresetName.Trim()))
        {
            RefreshPresetNames();
            SelectedPresetName = NewPresetName.Trim();
            NewPresetName = string.Empty;
        }
    }
}
