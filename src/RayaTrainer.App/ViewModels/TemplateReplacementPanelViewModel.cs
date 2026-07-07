using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.ViewModels;

public sealed class TemplateReplacementPanelViewModel : ViewModelBase
{
    private readonly TrainerFeature _modelFeature;
    private readonly TrainerFeature _weaponFeature;
    private readonly Func<ITrainerFeatureController?> _controllerProvider;
    private readonly Func<bool> _canExecute;
    private readonly Action<string> _setStatusMessage;
    private string _targetUnitIdText = "0x00000000";
    private string _donorUnitIdText = "0x00000000";

    public TemplateReplacementPanelViewModel(
        TrainerFeature modelFeature,
        TrainerFeature weaponFeature,
        Func<ITrainerFeatureController?> controllerProvider,
        Func<bool> canExecute,
        Action<string> setStatusMessage)
    {
        _modelFeature = modelFeature;
        _weaponFeature = weaponFeature;
        _controllerProvider = controllerProvider;
        _canExecute = canExecute;
        _setStatusMessage = setStatusMessage;
        ReplaceModelCommand = new RelayCommand(() => _ = ReplaceModelAsync(), CanExecute);
        ReplaceWeaponCommand = new RelayCommand(() => _ = ReplaceWeaponAsync(), CanExecute);
    }

    public RelayCommand ReplaceModelCommand { get; }

    public RelayCommand ReplaceWeaponCommand { get; }

    public string TargetUnitIdText
    {
        get => _targetUnitIdText;
        set
        {
            _targetUnitIdText = value;
            OnPropertyChanged();
        }
    }

    public string DonorUnitIdText
    {
        get => _donorUnitIdText;
        set
        {
            _donorUnitIdText = value;
            OnPropertyChanged();
        }
    }

    public string TargetUnitIdHelpText => "被替换模板的单位 ID；主要影响后续创建的同模板单位。";

    public string DonorUnitIdHelpText => "来源模板的单位 ID；用于提供模型记录或 WeaponSetUpdate 槽定义。";

    public string ReplaceTemplateModelHelpText => "按目标/来源单位模板 ID 执行模板级模型替换；MVP 不修改已在场单位实例，也不复制武器、炮塔或特殊模块。";

    public string ReplaceTemplateWeaponHelpText => "按目标/来源单位模板 ID 执行模板级武器模块替换；MVP 复用来源模板的 WeaponSetUpdate 槽定义，主要影响后续创建的同模板单位。";

    public TemplateModelReplacementSettings GetTemplateModelReplacementSettings()
    {
        return TemplateModelReplacementSettings.Parse(TargetUnitIdText, DonorUnitIdText);
    }

    public TemplateWeaponReplacementSettings GetTemplateWeaponReplacementSettings()
    {
        return TemplateWeaponReplacementSettings.Parse(TargetUnitIdText, DonorUnitIdText);
    }

    public async Task ReplaceModelAsync()
    {
        var controller = _controllerProvider();
        if (controller is null)
        {
            _setStatusMessage("请先检测进程并安装 patch。");
            return;
        }

        try
        {
            var settings = GetTemplateModelReplacementSettings();
            controller.WriteTemplateModelReplacementSettings(settings);
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                _modelFeature,
                timeout: FeatureDispatchDefaults.Timeout,
                pollInterval: FeatureDispatchDefaults.PollInterval);
            _setStatusMessage(result == ActionDispatchResult.Consumed
                ? $"已派发模板模型替换：{UnitCodeParser.Format(settings.TargetUnitId)} <- {UnitCodeParser.Format(settings.DonorUnitId)}。"
                : "模板模型替换动作已写入，但尚未被游戏循环消费。");
        }
        catch (Exception ex)
        {
            _setStatusMessage($"模板模型替换失败：{ex.Message}");
        }
    }

    public async Task ReplaceWeaponAsync()
    {
        var controller = _controllerProvider();
        if (controller is null)
        {
            _setStatusMessage("请先检测进程并安装 patch。");
            return;
        }

        try
        {
            var settings = GetTemplateWeaponReplacementSettings();
            controller.WriteTemplateWeaponReplacementSettings(settings);
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                _weaponFeature,
                timeout: FeatureDispatchDefaults.Timeout,
                pollInterval: FeatureDispatchDefaults.PollInterval);
            _setStatusMessage(result == ActionDispatchResult.Consumed
                ? $"已派发模板武器替换：{UnitCodeParser.Format(settings.TargetUnitId)} <- {UnitCodeParser.Format(settings.DonorUnitId)}。"
                : "模板武器替换动作已写入，但尚未被游戏循环消费。");
        }
        catch (Exception ex)
        {
            _setStatusMessage($"模板武器替换失败：{ex.Message}");
        }
    }

    public void RaiseCommandStates()
    {
        ReplaceModelCommand.RaiseCanExecuteChanged();
        ReplaceWeaponCommand.RaiseCanExecuteChanged();
    }

    private bool CanExecute()
    {
        return _canExecute() && _controllerProvider() is not null;
    }
}
