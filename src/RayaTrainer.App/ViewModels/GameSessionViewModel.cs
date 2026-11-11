using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 游戏会话状态视图模型。从 MainViewModel 提取。
/// 负责：游戏模式轮询（菜单/战役/遭遇战）和秘密协议绑定探针结果格式化。
/// </summary>
public sealed class GameSessionViewModel : ViewModelBase
{
    public const string SecretProtocolBindingProbeRawName = TrainerFeatureIds.SecretProtocolBindingProbe;

    private readonly Func<bool> _arePatchesInstalled;
    private readonly Func<ITrainerFeatureController?> _getController;
    private readonly Action<string> _setStatus;

    private bool _isInGame;
    private string _gameStateText = "未在对局";

    public GameSessionViewModel(
        Func<bool> arePatchesInstalled,
        Func<ITrainerFeatureController?> getController,
        Action<string> setStatus)
    {
        _arePatchesInstalled = arePatchesInstalled;
        _getController = getController;
        _setStatus = setStatus;
    }

    public bool IsInGame { get => _isInGame; private set { if (_isInGame == value) return; _isInGame = value; OnPropertyChanged(); } }

    public string GameStateText { get => _gameStateText; private set { if (_gameStateText == value) return; _gameStateText = value; OnPropertyChanged(); } }

    /// <summary>
    /// 在动作派发完成后，若是秘密协议绑定探针动作，读取探针结果并格式化到状态栏。
    /// </summary>
    public void CompleteActionIfNeeded(TrainerFeature feature, ActionDispatchResult dispatchResult)
    {
        if (!feature.RawName.Equals(SecretProtocolBindingProbeRawName, StringComparison.Ordinal) ||
            dispatchResult != ActionDispatchResult.Consumed ||
            _getController() is null)
        {
            return;
        }

        var result = _getController()!.ReadSecretProtocolBindingProbeResult();
        _setStatus(result.Status switch
        {
            SecretProtocolBindingProbeStatus.Completed => $"秘密协议绑定验证：盟军 AirPower={FormatSecretProtocolStatus(result.AirPowerStatus)}，日本 EnhancedKamikaze={FormatSecretProtocolStatus(result.EnhancedKamikazeStatus)}。",
            SecretProtocolBindingProbeStatus.MissingTemplate => "秘密协议绑定验证：固定 PlayerTech 模板未全部找到。",
            SecretProtocolBindingProbeStatus.NoPlayer => "秘密协议绑定验证：尚未取得玩家对象，请进入对局后再执行。",
            _ => "秘密协议绑定验证：未返回有效结果。"
        });
    }

    public void RefreshGameState()
    {
        if (!_arePatchesInstalled() || _getController() is null)
        {
            ResetGameState();
            return;
        }

        try
        {
            var gameMode = _getController()!.ReadGameMode();
            if (gameMode == GameRuntimeConstants.GameModeShell)
            {
                IsInGame = false;
                GameStateText = "未在对局";
            }
            else if (gameMode == 8)
            {
                IsInGame = true;
                GameStateText = "战役中";
            }
            else if (gameMode == 2)
            {
                IsInGame = true;
                GameStateText = "遭遇战";
            }
            else
            {
                IsInGame = true;
                GameStateText = "对局中";
            }
        }
        catch
        {
            ResetGameState();
        }
    }

    public void ResetGameState()
    {
        IsInGame = false;
        GameStateText = "未在对局";
    }

    private static string FormatSecretProtocolStatus(SecretProtocolBindingItemStatus status)
    {
        return status switch
        {
            SecretProtocolBindingItemStatus.TechAndUpgradeGranted => "协议和被动均生效",
            SecretProtocolBindingItemStatus.TechGrantedUpgradeManuallyGranted => "协议已拥有，被动已手动补授予",
            SecretProtocolBindingItemStatus.TechGrantedUpgradeMissing => "协议已拥有但被动未授予",
            SecretProtocolBindingItemStatus.TemplateMissing => "模板未找到",
            _ => "未运行"
        };
    }
}
