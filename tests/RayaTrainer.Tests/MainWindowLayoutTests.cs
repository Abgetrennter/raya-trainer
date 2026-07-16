using System.Xml.Linq;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainWindowLayoutTests
{
    private const string ProjectRoot = "src/RayaTrainer.App";

    [Fact]
    public void WindowWidthIsWideEnoughForSideBySideWithGame()
    {
        var document = LoadXaml("MainWindow.xaml");
        var window = document.Root ?? throw new InvalidOperationException("MainWindow.xaml has no root element.");

        Assert.Equal("1230", window.Attribute("Width")?.Value);
        Assert.Equal("960", window.Attribute("MinWidth")?.Value);
    }

    [Fact]
    public void ReinforcementRowControlsExistInPage()
    {
        var document = LoadXaml("Pages/ReinforcementPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "{Binding ReinforcementUnitIdText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding ReinforcementCountText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding ReinforcementRankText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Content", "单位列表"));
        Assert.NotNull(FindByAttribute(document, "Content", "{Binding ReadSelectedUnitCodeButtonText}"));
    }

    [Fact]
    public void ReinforcementPageExposesMigratedActionButtons()
    {
        var document = LoadXaml("Pages/ReinforcementPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Content", "{Binding GetMeBaseButtonText}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding GetMeBaseCommand}"));
        Assert.NotNull(FindByAttribute(document, "Content", "{Binding ExecuteReinforcementButtonText}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding ExecuteReinforcementCommand}"));
        Assert.NotNull(FindByAttribute(document, "Content", "{Binding CopySelectedUnitButtonText}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding CopySelectedUnitCommand}"));
    }

    [Fact]
    public void SecretProtocolGrantFieldControlsExistInPage()
    {
        var document = LoadXaml("Pages/SecretProtocolPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "{Binding SecretProtocolNameText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding SecretProtocolPlayerTechIdText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding SecretProtocolUpgradeIdText, UpdateSourceTrigger=PropertyChanged}"));
    }

    [Fact]
    public void SecretProtocolPageExposesProtocolAndSkillToggles()
    {
        var document = LoadXaml("Pages/SecretProtocolPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "协议与技能开关"));
        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding SecretProtocolToggles}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding DisplayName}"));
        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding IsFeatureEnabled, Mode=OneWay}"));
        Assert.True(HasAttribute(document, "Text", "{Binding Status}"));
    }

    [Fact]
    public void SecretProtocolPageExposesSecretProtocolHashSource()
    {
        var document = LoadXaml("Pages/SecretProtocolPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "{Binding SecretProtocolHashSourceText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding HashSecretProtocolSourceCommand}"));
    }

    [Fact]
    public void ToolsPageExposesUpdateCheckControls()
    {
        var document = LoadXaml("Pages/ToolsPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "{Binding CurrentVersionText}"));
        Assert.NotNull(FindByAttribute(document, "Content", "检查更新"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding CheckForUpdatesCommand}"));
        Assert.NotNull(FindByAttribute(document, "ToolTip", "{Binding CheckForUpdatesHelpText}"));
    }

    [Fact]
    public void ToolsPageExposesMobileRemoteQrCodeControls()
    {
        var document = LoadXaml("Pages/ToolsPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "手机遥控"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding MobileRemote.RemoteUrl, Mode=OneWay}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding MobileRemote.GenerateQrCodeCommand}"));
        Assert.NotNull(FindByAttribute(document, "Source", "{Binding MobileRemote.QrCodeImage}"));
    }

    [Fact]
    public void SidebarExposesSelectedUnitPage()
    {
        var document = LoadXaml("MainWindow.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "选中单位"));
        Assert.NotNull(FindByAttribute(document, "DataContext", "{Binding SelectedUnit}"));
        Assert.NotNull(FindByAttribute(document, "Visibility", "{Binding DataContext.SelectedPageIndex, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource PageVis}, ConverterParameter=1}"));
    }

    [Fact]
    public void SidebarExposesStatusEditorPage()
    {
        var document = LoadXaml("MainWindow.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "选中单位"));
        Assert.NotNull(FindByAttribute(document, "Text", "状态编辑器"));
        Assert.NotNull(FindByAttribute(document, "Visibility", "{Binding DataContext.SelectedPageIndex, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource PageVis}, ConverterParameter=5}"));
    }

    [Fact]
    public void SidebarExposesHotkeySettingsPage()
    {
        var window = LoadXaml("MainWindow.xaml");
        var page = LoadXaml("Pages/HotkeySettingsPage.xaml");

        // 侧边栏导航项存在（显示文本为"设置快捷键"）。
        Assert.NotNull(FindByAttribute(window, "Text", "设置快捷键"));
        // 内容区按 ConverterParameter=7 切换可见性。
        Assert.NotNull(FindByAttribute(window, "Visibility", "{Binding DataContext.SelectedPageIndex, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource PageVis}, ConverterParameter=7}"));
        // 设置页绑定到 MainViewModel.HotkeySettings。
        Assert.NotNull(FindByAttribute(window, "DataContext", "{Binding HotkeySettings}"));
        // 设置页核心控件：捕获控件 + 保存/恢复按钮。
        Assert.NotNull(FindByAttribute(page, "HotkeyText", "{Binding CurrentHotkey, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(page, "Command", "{Binding ResetAllCommand}"));
        Assert.NotNull(FindByAttribute(page, "Command", "{Binding SaveCommand}"));
    }

    [Fact]
    public void WindowExposesDiagnosticsNavigationHealthAndRuntimeChain()
    {
        var window = LoadXaml("MainWindow.xaml");
        var diagnostics = LoadXaml("Pages/DiagnosticsPage.xaml");

        Assert.NotNull(FindByAttribute(window, "Text", "诊断"));
        Assert.True(HasAttribute(window, "Command", "{Binding OpenDiagnosticsCommand}"));
        Assert.NotNull(FindByAttribute(window, "Visibility", "{Binding DataContext.SelectedPageIndex, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource PageVis}, ConverterParameter=6}"));
        Assert.NotNull(FindByAttribute(diagnostics, "ItemsSource", "{Binding Stages}"));
        Assert.NotNull(FindByAttribute(diagnostics, "Command", "{Binding RefreshCommand}"));
        Assert.NotNull(FindByAttribute(diagnostics, "Command", "{Binding ExportCommand}"));
        Assert.NotNull(FindByAttribute(diagnostics, "ItemsSource", "{Binding CapabilityGroups}"));
        Assert.NotNull(FindByAttribute(diagnostics, "ItemsSource", "{Binding Events}"));
    }

    [Fact]
    public void WindowMakesTheNoviceNextStepPrimaryAndFoldsExpertControls()
    {
        var window = LoadXaml("MainWindow.xaml");

        Assert.NotNull(FindByAttribute(window, "Text", "{Binding PrimaryActionStepText}"));
        Assert.NotNull(FindByAttribute(window, "Content", "{Binding PrimaryActionTitle}"));
        Assert.NotNull(FindByAttribute(window, "Command", "{Binding PrimaryActionCommand}"));
        Assert.NotNull(FindByAttribute(window, "IsExpanded", "{Binding IsGameSetupExpanded, Mode=TwoWay}"));
        var advancedControls = FindByAttribute(window, "Header", "高级控制（通常不需要）");
        Assert.NotNull(advancedControls);
        Assert.Equal("{DynamicResource BrushTextPrimary}", advancedControls.Attribute("Foreground")?.Value);
    }

    [Fact]
    public void StatusEditorPageExposesFiltersAndWriteButtons()
    {
        var document = LoadXaml("Pages/StatusEditorPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "{Binding StatusBitEditor.SearchText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding StatusBitEditor.DomainOptions}"));
        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding StatusBitEditor.CategoryOptions}"));
        Assert.NotNull(FindByAttribute(document, "Content", "显示全部字段"));
        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding StatusBitEditor.ShowAllStatusFields, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding StatusBitEditor.FilteredStatuses}"));
        Assert.NotNull(FindByAttribute(document, "Text", "说明"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding HelpText}"));
        Assert.False(HasAttribute(document, "IsChecked", "{Binding StatusBitEditor.ShowRecommendedOnly, UpdateSourceTrigger=PropertyChanged}"));
        Assert.False(HasAttribute(document, "IsChecked", "{Binding StatusBitEditor.ShowDangerousStatuses, UpdateSourceTrigger=PropertyChanged}"));
        Assert.False(HasAttribute(document, "ToolTip", "{Binding Definition.HelpText}"));
        Assert.NotNull(FindByAttribute(document, "Content", "置 1"));
        Assert.NotNull(FindByAttribute(document, "Content", "清 0"));
    }

    [Fact]
    public void StatusEditorPageUsesVirtualizedList()
    {
        var document = LoadXaml("Pages/StatusEditorPage.xaml");

        var list = FindByAttribute(document, "ItemsSource", "{Binding StatusBitEditor.FilteredStatuses}");

        Assert.Equal("ListBox", list.Name.LocalName);
        Assert.Equal("True", list.Attribute("VirtualizingPanel.IsVirtualizing")?.Value);
        Assert.Equal("Recycling", list.Attribute("VirtualizingPanel.VirtualizationMode")?.Value);
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ItemsControl");
    }

    [Fact]
    public void MainViewModelDoesNotStartAutomaticGameStatePolling()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            ProjectRoot,
            "ViewModels",
            "MainViewModel.cs"));

        Assert.DoesNotContain("_gameStatePollTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGameStatePollTimerTick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartGameStatePollTimer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretProtocolPanelOpensPickerWindowInsteadOfEmbeddingProtocolLists()
    {
        var document = LoadXaml("Pages/SecretProtocolPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Content", "协议列表"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding OpenSecretProtocolPickerCommand}"));
    }

    [Fact]
    public void SecretProtocolPanelExposesSelectedBuildingUpgradeGrantButton()
    {
        var document = LoadXaml("Pages/SecretProtocolPage.xaml");

        var button = FindByAttribute(document, "Content", "授予选中建筑");

        Assert.Equal("{Binding GrantSelectedObjectUpgradeCommand}", button.Attribute("Command")?.Value);
        Assert.Equal("{Binding GrantSelectedObjectUpgradeHelpText}", button.Attribute("ToolTip")?.Value);
    }

    [Fact]
    public void ToolbarDoesNotExposeRuntimeSelector()
    {
        var document = LoadXaml("MainWindow.xaml");

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute("SelectedValue")?.Value.Contains("SelectedRuntime", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ToolbarExposesModSelectionWithoutSeparateDirectLaunchButton()
    {
        var document = LoadXaml("MainWindow.xaml");

        var selector = FindByAttribute(document, "ItemsSource", "{Binding GameLaunch.ModLaunchEntries}");

        Assert.Equal("{Binding GameLaunch.SelectedModLaunchEntry, UpdateSourceTrigger=PropertyChanged}", selector.Attribute("SelectedItem")?.Value);
        Assert.Equal("DisplayName", selector.Attribute("DisplayMemberPath")?.Value);
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding GameLaunch.ModsRootPath, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding GameLaunch.BrowseModsRootCommand}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding GameLaunch.RefreshModsCommand}"));
        Assert.False(HasAttribute(document, "Command", "{Binding DirectModLaunchAndLoadCommand}"));
    }

    [Fact]
    public void ToolbarExposesStructuredLauncherArgumentControls()
    {
        var document = LoadXaml("MainWindow.xaml");

        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding GameLaunch.LaunchUseRa3LauncherUi, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding GameLaunch.LaunchWindowed, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding GameLaunch.LaunchFullscreen, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding GameLaunch.LaunchBorderlessFullscreen, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding GameLaunch.LaunchResolutionXText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding GameLaunch.LaunchResolutionYText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding GameLaunch.LaunchWindowPositionXText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding GameLaunch.LaunchWindowPositionYText, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding GameLaunch.LaunchNoAudio, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "IsChecked", "{Binding GameLaunch.LaunchNoAudioMusic, UpdateSourceTrigger=PropertyChanged}"));
        Assert.False(HasAttribute(document, "Text", "{Binding LaunchExtraArgumentsText, UpdateSourceTrigger=PropertyChanged}"));
    }

    [Fact]
    public void ToolbarExposesFinalLauncherArgumentsAndGenerateButton()
    {
        var document = LoadXaml("MainWindow.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "{Binding GameLaunch.LauncherArguments, UpdateSourceTrigger=PropertyChanged}"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding GameLaunch.GenerateLauncherArgumentsCommand}"));
        // 装载并启动按钮 Content 绑定到 MainViewModel.LaunchAndLoadButtonText，
        // 该属性会动态附加已配置的全局热键（如「装载并启动 (Ctrl+Alt+L)」）。
        Assert.NotNull(FindByAttribute(document, "Content", "{Binding LaunchAndLoadButtonText}"));
        // 立刻检测按钮同理。
        Assert.NotNull(FindByAttribute(document, "Content", "{Binding RefreshProcessButtonText}"));
    }

    [Fact]
    public void SelectedUnitPageExposesUnitUpgradeSubpanel()
    {
        var document = LoadXaml("Pages/SelectedUnitPage.xaml");

        Assert.NotNull(FindByAttribute(document, "Text", "单位升级"));
        Assert.NotNull(FindByAttribute(document, "Content", "刷新可用升级"));
        Assert.NotNull(FindByAttribute(document, "Command", "{Binding UnitUpgrade.RefreshCommand}"));
        Assert.NotNull(FindByAttribute(document, "ItemsSource", "{Binding UnitUpgrade.AvailableUpgrades}"));
        Assert.NotNull(FindByAttribute(document, "Text", "{Binding UnitUpgrade.StatusMessage}"));
        Assert.NotNull(FindByAttribute(document, "Visibility", "{Binding UnitUpgrade.IsListVisible, Converter={StaticResource BoolVis}}"));
        Assert.True(HasAttribute(document, "Content", "授予"));
        Assert.True(HasAttribute(document, "CommandParameter", "{Binding}"));
    }

    [Fact]
    public void MainWindowWiresWindowGeometryPersistenceAndExitFlush()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            ProjectRoot,
            "MainWindow.xaml.cs"));

        Assert.Contains("OnSourceInitializedRestoreBounds", source, StringComparison.Ordinal);
        Assert.Contains("OnWindowGeometryChanged", source, StringComparison.Ordinal);
        Assert.Contains("OnClosingFlush", source, StringComparison.Ordinal);
        Assert.Contains("UpdateWindowBounds", source, StringComparison.Ordinal);
        Assert.Contains("LastWindowBounds", source, StringComparison.Ordinal);
    }

    private static XDocument LoadXaml(string relativePath)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            ProjectRoot,
            relativePath);
        return XDocument.Load(path);
    }

    private static XElement FindByAttribute(XContainer document, string name, string value)
    {
        return document
            .Descendants()
            .Single(element => element.Attribute(name)?.Value == value);
    }

    private static bool HasAttribute(XContainer document, string name, string value)
    {
        return document
            .Descendants()
            .Any(element => element.Attribute(name)?.Value == value);
    }
}
