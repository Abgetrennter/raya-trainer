using RayaTrainer.App.Services;
using RayaTrainer.Host.Services;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Hotkeys;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

// MainViewModel 的热键域 partial：声明表、绑定构建、全局热键注册与运行时热重载。
// 拆分自 god-file（Debt 4），沿用 MainViewModel.Launch.cs 的 partial 模式。
public sealed partial class MainViewModel
{
    private void StartHotkeys()
    {
        _hotkeyCoordinator.Start(AllFeatureItems(), CreateActionHotkeyBindings(_hotkeys));
    }

    /// <summary>
    /// 初始化全局热键服务。需在主窗口 HWND 创建后（OnSourceInitialized）调用一次。
    /// 在此之前 ReloadHotkeys 仅更新内存状态，不调用 Win32 RegisterHotKey。
    /// </summary>
    public void InitializeGlobalHotkeys(IntPtr hwnd)
    {
        if (_globalHotkeyService is not null)
        {
            return; // 已初始化（例如窗口重建场景），保持首次实例避免重复 hook。
        }
        _globalHotkeyService = new GlobalHotkeyService(hwnd);
        ApplyGlobalHotkeys();
    }

    /// <summary>
    /// 按声明表中的全局动作当前配置重新注册全局热键。
    /// 在 InitializeGlobalHotkeys 和 ReloadHotkeys 中调用；未分配（解析为 null）的动作跳过注册。
    /// </summary>
    private void ApplyGlobalHotkeys()
    {
        if (_globalHotkeyService is null)
        {
            return;
        }
        _globalHotkeyService.UnregisterAll();

        // 全局动作按声明表顺序从基址分配 id；注册失败逐条提示（组合键被占用）。
        var globalIndex = 0;
        foreach (var definition in _actionHotkeyDefinitions)
        {
            if (definition.Scope != ActionHotkeyScope.GlobalRegister ||
                ResolveConfiguredHotkey(_hotkeys, definition.RawName) is not { } gesture)
            {
                continue;
            }

            if (!_globalHotkeyService.Register(GlobalHotkeyIdBase + globalIndex++, gesture, definition.Execute))
            {
                StatusMessage = $"全局热键 {gesture.DisplayText} 注册失败，可能被其他程序占用；可在设置页改用其他组合。";
            }
        }
    }

    /// <summary>
    /// 动作热键声明表：新增主要按键只需在此加一条记录。默认组合选 Ctrl+Alt 是为避开
    /// Ctrl+Shift（系统键盘布局切换）、Win+*（系统级）、Alt+letter（菜单助记符）等高频冲突，
    /// 用户可在设置页改成任何组合。
    /// </summary>
    private IReadOnlyList<ActionHotkeyDefinition> CreateActionHotkeyDefinitions()
    {
        return
        [
            new(TrainerFeatureIds.ExecuteReinforcementQueue, "执行队列（增援）", "Insert",
                () => Reinforcement.ExecuteReinforcementQueueCommand.Execute(null),
                () => Reinforcement.ExecuteReinforcementQueueCommand.CanExecute(null),
                GestureChanged: text => Reinforcement.ExecuteReinforcementQueueButtonText = FormatLabelWithHotkey("执行队列", text)),
            new(TrainerFeatureIds.ReadSelectedUnitCode, "读取选中单位代码", "Home",
                () => Reinforcement.ReadSelectedUnitCodeCommand.Execute(null),
                () => Reinforcement.ReadSelectedUnitCodeCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.ReadSelectedUnitCodeButtonText = FormatLabelWithHotkey("读取选中单位", text)),
            new(TrainerFeatureIds.AscensionApply, "属性修改：应用", "Ctrl+Alt+A",
                () => Ascension.ApplyCommand.Execute(null),
                () => Ascension.ApplyCommand.CanExecute(null),
                GestureChanged: text => Ascension.ApplyButtonText = FormatLabelWithHotkey("应用", text)),
            new(TrainerFeatureIds.AscensionRestore, "属性修改：全部还原", "Ctrl+Alt+R",
                () => Ascension.RestoreCommand.Execute(null),
                () => Ascension.RestoreCommand.CanExecute(null),
                GestureChanged: text => Ascension.RestoreButtonText = FormatLabelWithHotkey("全部还原", text)),
            // 游戏内面板显隐：命中即消费该键，使原生 WM_KEYDOWN F10 处理器收不到，
            // 避免主菜单里双重切换互相抵消。
            new(TrainerFeatureIds.ToggleOverlay, "游戏内面板 显示/隐藏", "F10",
                () => _ = _sessionManager.ToggleOverlayVisibilityAsync(),
                () => _sessionManager.CanToggleOverlay),
            // 主控操作（全局）：修改器最小化或游戏未启动时也能触发，走 Win32 RegisterHotKey。
            new(TrainerFeatureIds.DetectProcess, "立刻检测（全局）", "Ctrl+Alt+D",
                () => { if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null); },
                CanExecute: null,
                Scope: ActionHotkeyScope.GlobalRegister),
            new(TrainerFeatureIds.LaunchAndLoad, "装载并启动（全局）", "Ctrl+Alt+L",
                () => { if (LaunchAndLoadCommand.CanExecute(null)) LaunchAndLoadCommand.Execute(null); },
                CanExecute: null,
                Scope: ActionHotkeyScope.GlobalRegister),
        ];
    }

    private IReadOnlyList<FeatureCommandOverride> CreateFeatureCommandOverrides()
    {
        return
        [
            new(TrainerFeatureIds.GetBase,
                () => Reinforcement.GetMeBaseCommand.Execute(null),
                () => Reinforcement.GetMeBaseCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.GetMeBaseButtonText = FormatLabelWithHotkey("给玩家基地车", text)),
            new(TrainerFeatureIds.Reinforcement,
                () => Reinforcement.ExecuteReinforcementCommand.Execute(null),
                () => Reinforcement.ExecuteReinforcementCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.ExecuteReinforcementButtonText = FormatLabelWithHotkey("呼叫战场增援", text)),
            new(TrainerFeatureIds.CopySelectedUnit,
                () => Reinforcement.CopySelectedUnitCommand.Execute(null),
                () => Reinforcement.CopySelectedUnitCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.CopySelectedUnitButtonText = FormatLabelWithHotkey("复制选中单位", text)),
        ];
    }

    private IReadOnlyDictionary<string, string> CreateDefaultHotkeys(IReadOnlyList<TrainerFeature> features)
    {
        // 目录功能默认键来自 feature.Hotkey（经 manifest/catalog），动作热键默认键来自声明表。
        var hotkeys = new Dictionary<string, string>(TrainerFeatureCatalog.CreateDefaultHotkeys(features), StringComparer.Ordinal);
        foreach (var definition in _actionHotkeyDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(definition.DefaultGesture))
            {
                hotkeys[definition.RawName] = definition.DefaultGesture;
            }
        }

        return hotkeys;
    }

    private static HotkeyGesture? ResolveConfiguredHotkey(IReadOnlyDictionary<string, string> hotkeys, string name)
    {
        if (hotkeys.TryGetValue(name, out var hotkey))
            return HotkeyGesture.TryParse(hotkey, out var gesture) ? gesture : null;
        return null;
    }

    /// <summary>
    /// 构建动作热键绑定：声明表动作按 <paramref name="hotkeys"/> 现场解析（无缓存手势字段，
    /// 热重载后重建即生效）；feature 命令覆盖的手势经覆盖应用后的 feature 解析，
    /// 无用户覆盖时回落 feature 默认键。internal 供测试断言绑定新鲜度。
    /// </summary>
    internal IEnumerable<HotkeyActionBinding> CreateActionHotkeyBindings(IReadOnlyDictionary<string, string> hotkeys)
    {
        foreach (var definition in _actionHotkeyDefinitions)
        {
            if (ResolveConfiguredHotkey(hotkeys, definition.RawName) is not { } gesture)
            {
                continue;
            }

            yield return new HotkeyActionBinding(gesture, definition.Execute, definition.CanExecute, definition.AllowRepeat);
        }

        var configuredFeatures = TrainerFeatureCatalog.ApplyHotkeyOverrides(_uiFeatures, hotkeys);
        var featureByRawName = configuredFeatures.ToDictionary(f => f.RawName, StringComparer.Ordinal);
        foreach (var overrideEntry in _featureCommandOverrides)
        {
            if (!featureByRawName.TryGetValue(overrideEntry.RawName, out var feature) ||
                !HotkeyGesture.TryParse(feature.Hotkey, out var featureGesture))
            {
                continue;
            }

            yield return new HotkeyActionBinding(featureGesture, overrideEntry.Execute, overrideEntry.CanExecute, overrideEntry.AllowRepeat);
        }
    }

    private static string FormatActionButtonText(TrainerFeature feature, string label)
    {
        return FormatLabelWithHotkey(label, feature.Hotkey);
    }

    private static string FormatLabelWithHotkey(string label, string? hotkey)
    {
        return string.IsNullOrWhiteSpace(hotkey) ? label : $"{label} ({hotkey})";
    }

    private string FormatActionHotkeyText(string rawName, string label)
    {
        return FormatLabelWithHotkey(label, ResolveConfiguredHotkey(_hotkeys, rawName)?.DisplayText);
    }

    /// <summary>把声明表动作热键的当前组合经 GestureChanged 同步到各按钮文本（构造初始呈现与热重载共用）。</summary>
    private void RefreshActionHotkeyTexts()
    {
        foreach (var definition in _actionHotkeyDefinitions)
        {
            definition.GestureChanged?.Invoke(ResolveConfiguredHotkey(_hotkeys, definition.RawName)?.DisplayText);
        }
    }

    private void StopHotkeys() => _hotkeyCoordinator.Stop();

    /// <summary>
    /// 运行时热重载热键：用户在设置页改键后无需重启程序即可生效。
    /// 更新内存字典、刷新所有 UI 显示、若 patch 已安装则重建 dispatcher bindings，并持久化到配置文件。
    /// </summary>
    public void ReloadHotkeys(IReadOnlyDictionary<string, string> newHotkeys)
    {
        _hotkeys = new Dictionary<string, string>(newHotkeys, StringComparer.Ordinal);

        // 重新解析 feature 热键（基于 RawName），并按 RawName 把新值写回每个 FeatureItemViewModel。
        var configured = TrainerFeatureCatalog.ApplyHotkeyOverrides(_uiFeatures, _hotkeys);
        var hotkeyByRawName = configured.ToDictionary(f => f.RawName, f => f.Hotkey, StringComparer.Ordinal);
        foreach (var item in AllFeatureItems())
        {
            if (hotkeyByRawName.TryGetValue(item.Feature.RawName, out var hotkey))
            {
                item.RefreshHotkey(hotkey);
            }
            else
            {
                item.RefreshHotkey(null);
            }
        }

        // feature 命令覆盖的按钮文本随覆盖应用后的 feature.Hotkey 刷新。
        foreach (var overrideEntry in _featureCommandOverrides)
        {
            if (hotkeyByRawName.TryGetValue(overrideEntry.RawName, out var overrideHotkey))
            {
                overrideEntry.GestureChanged?.Invoke(overrideHotkey);
            }
        }

        // 声明表动作热键重解析，经 GestureChanged 刷新各自呈现；随后统一通知主控按钮文本并重注册全局键。
        RefreshActionHotkeyTexts();

        OnPropertyChanged(nameof(RefreshProcessButtonText));
        OnPropertyChanged(nameof(LaunchAndLoadButtonText));
        ApplyGlobalHotkeys();

        // 仅当 patch 已安装（dispatcher 实际在跑）时才重建 bindings，避免对未启动会话误装钩子。
        if (ArePatchesInstalled)
        {
            StopHotkeys();
            StartHotkeys();
        }

        PersistSettings();
    }
}
