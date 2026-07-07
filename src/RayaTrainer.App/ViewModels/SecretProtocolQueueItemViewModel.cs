using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels;

public sealed class SecretProtocolQueueItemViewModel : ViewModelBase
{
    private string _status = "等待";
    private string _message = string.Empty;

    public SecretProtocolQueueItemViewModel(
        SecretProtocolEntry protocol,
        Action<SecretProtocolQueueItemViewModel> remove,
        Func<bool> canRemove)
    {
        Protocol = protocol;
        RemoveCommand = new RelayCommand(() => remove(this), canRemove);
    }

    public SecretProtocolEntry Protocol { get; }

    public string Faction => Protocol.Faction;

    public string Name => Protocol.Name;

    public string PlayerTechIdText => Protocol.PlayerTechIdText;

    public string UpgradeText => Protocol.UpgradeText;

    public string RemoveQueueItemHelpText => "从秘密协议添加列表移除此项，不影响已授予结果。";

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            OnPropertyChanged();
        }
    }

    public RelayCommand RemoveCommand { get; }

    public SecretProtocolQueueEntry ToEntry() => new(Protocol);

    public void ApplyResult(SecretProtocolQueueResult result)
    {
        Status = result.Status switch
        {
            SecretProtocolQueueItemStatus.Executed => "已执行",
            SecretProtocolQueueItemStatus.TimedOut => "超时",
            SecretProtocolQueueItemStatus.Failed => "失败",
            SecretProtocolQueueItemStatus.Executing => "执行中",
            SecretProtocolQueueItemStatus.AbortedDueToPause => "已放弃（游戏暂停）",
            _ => "等待"
        };
        Message = result.Message;
    }

    public void RaiseCommandState()
    {
        RemoveCommand.RaiseCanExecuteChanged();
    }
}
