namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 功能分组。IsExpanded 控制 FeaturesPage ActionCard 的折叠/展开状态。
/// </summary>
public sealed class FeatureGroupViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public FeatureGroupViewModel(
        string groupId,
        string name,
        IReadOnlyList<FeatureItemViewModel> features,
        bool isExpanded = true)
    {
        GroupId = groupId;
        Name = name;
        Features = features;
        _isExpanded = isExpanded;
    }

    public string GroupId { get; }

    public string Name { get; }

    public IReadOnlyList<FeatureItemViewModel> Features { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }
}
