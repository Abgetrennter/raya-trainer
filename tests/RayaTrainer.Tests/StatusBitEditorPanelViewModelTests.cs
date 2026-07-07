using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class StatusBitEditorPanelViewModelTests
{
    [Fact]
    public void DefaultFilterShowsRecommendedModifierCandidatesAndCanSearchByName()
    {
        var viewModel = CreateViewModel();

        Assert.All(viewModel.FilteredStatuses, row => Assert.True(row.Definition.IsRecommendedFunction));
        Assert.Contains(viewModel.FilteredStatuses, row => row.Definition.Name == "STEALTHED");
        Assert.DoesNotContain(viewModel.FilteredStatuses, row => row.Definition.Name == "CAN_ATTACK");

        viewModel.SearchText = "iron";

        Assert.Contains(viewModel.FilteredStatuses, row => row.Definition.Name == "UNDER_IRON_CURTAIN");
        Assert.DoesNotContain(viewModel.FilteredStatuses, row => row.Definition.Name == "IRONCURTAIN");
    }

    [Fact]
    public void ShowingAllStatusFieldsIncludesNonRecommendedAndHiddenEntries()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "destroyed";
        Assert.DoesNotContain(viewModel.FilteredStatuses, row => row.Definition.Name == "DESTROYED");

        viewModel.ShowAllStatusFields = true;

        Assert.Contains(viewModel.FilteredStatuses, row =>
            row.Definition.Domain == StatusBitDomain.ObjectStatus &&
            row.Definition.Name == "DESTROYED");
    }

    [Fact]
    public void ShowingAllStatusFieldsIncludesReferenceNoEffectStates()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "IRONCURTAIN";
        Assert.DoesNotContain(viewModel.FilteredStatuses, row => row.Definition.Name == "IRONCURTAIN");

        viewModel.ShowAllStatusFields = true;

        Assert.Contains(viewModel.FilteredStatuses, row =>
            row.Definition.Domain == StatusBitDomain.ModelConditionFlags &&
            row.Definition.Name == "IRONCURTAIN");
    }

    [Fact]
    public void DomainFilterLimitsRowsToSelectedDomain()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedDomain = viewModel.DomainOptions.Single(option =>
            option.Domain == StatusBitDomain.ModelConditionFlags);

        Assert.All(viewModel.FilteredStatuses, row =>
            Assert.Equal(StatusBitDomain.ModelConditionFlags, row.Definition.Domain));
    }

    [Fact]
    public void DefaultFilterShowsFirstBatchModifierCandidates()
    {
        var viewModel = CreateViewModel();

        Assert.Contains(viewModel.FilteredStatuses, row => row.Definition.Name == "STEALTHED");
        Assert.Contains(viewModel.FilteredStatuses, row => row.Definition.Name == "UNDER_IRON_CURTAIN");
        Assert.Contains(viewModel.FilteredStatuses, row => row.Definition.Name == "WEAPONSET_HERO");
        Assert.Contains(viewModel.FilteredStatuses, row => row.Definition.Name == "ARMORSET_HERO");
        Assert.DoesNotContain(viewModel.FilteredStatuses, row => row.Definition.Name == "INVULNERABLE");
        Assert.DoesNotContain(viewModel.FilteredStatuses, row => row.Definition.Name == "IRONCURTAIN");
    }

    [Fact]
    public void DefaultRecommendedFilterStillHonorsDomainAndSearch()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedDomain = viewModel.DomainOptions.Single(option =>
            option.Domain == StatusBitDomain.ModelConditionFlags);
        viewModel.SearchText = "armor";

        Assert.All(viewModel.FilteredStatuses, row =>
        {
            Assert.Equal(StatusBitDomain.ModelConditionFlags, row.Definition.Domain);
            Assert.Contains("ARMOR", row.Definition.Name, StringComparison.OrdinalIgnoreCase);
            Assert.True(row.Definition.IsRecommendedFunction);
        });
    }

    [Fact]
    public void RowsExposeCompactHelpTextWithoutRepeatedWriteBoilerplate()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.FilteredStatuses.Single(item => item.Definition.Name == "UNDER_IRON_CURTAIN");

        Assert.Contains("吸收除了HEALING和UNRESISTABLE外的一切伤害", row.HelpText);
        Assert.DoesNotContain("ObjectStatus bit", row.HelpText);
        Assert.DoesNotContain("分类：", row.HelpText);
        Assert.DoesNotContain("本面板", row.HelpText);
    }

    [Fact]
    public async Task ApplySetAndClearCallWriterWithSelectedStatusBit()
    {
        var writes = new List<(StatusBitDomain Domain, uint BitIndex, bool Enabled)>();
        var viewModel = CreateViewModel(out var lastMessage, (definition, enabled) =>
        {
            writes.Add((definition.Domain, definition.BitIndex, enabled));
            return Task.FromResult(GameApiDispatchStatus.Completed);
        });
        var row = viewModel.FilteredStatuses.Single(item => item.Definition.Name == "STEALTHED");

        await viewModel.ApplyAsync(row, enabled: true);
        await viewModel.ApplyAsync(row, enabled: false);

        Assert.Equal(
            [
                (StatusBitDomain.ObjectStatus, 17u, true),
                (StatusBitDomain.ObjectStatus, 17u, false)
            ],
            writes);
        Assert.Equal("Completed", row.LastResult);
        Assert.Contains("STEALTHED", lastMessage());
    }

    [Fact]
    public async Task ApplyReturnsBeforeAsyncWriterCompletes()
    {
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerCanComplete = new TaskCompletionSource<GameApiDispatchStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(async (_, _) =>
        {
            writerStarted.SetResult();
            return await writerCanComplete.Task;
        });
        var row = viewModel.FilteredStatuses.Single(item => item.Definition.Name == "STEALTHED");

        var applyTask = viewModel.ApplyAsync(row, enabled: true);
        await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(applyTask.IsCompleted);

        writerCanComplete.SetResult(GameApiDispatchStatus.Completed);
        await applyTask;
        Assert.Equal("Completed", row.LastResult);
    }

    [Fact]
    public void CommandsAreDisabledWhenAgentOnlyGateIsClosed()
    {
        var viewModel = CreateViewModel(canExecute: () => false);
        var row = viewModel.FilteredStatuses.Single(item => item.Definition.Name == "STEALTHED");

        Assert.False(row.SetCommand.CanExecute(null));
        Assert.False(row.ClearCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyFailureUpdatesRowAndStatusMessage()
    {
        var viewModel = CreateViewModel(out var lastMessage, (_, _) => Task.FromException<GameApiDispatchStatus>(new InvalidOperationException("agent missing")));
        var row = viewModel.FilteredStatuses.Single(item => item.Definition.Name == "STEALTHED");

        await viewModel.ApplyAsync(row, enabled: true);

        Assert.Equal("失败", row.LastResult);
        Assert.Contains("agent missing", lastMessage());
    }

    private static StatusBitEditorPanelViewModel CreateViewModel(
        Func<StatusBitDefinition, bool, Task<GameApiDispatchStatus>>? writer = null,
        Func<bool>? canExecute = null)
    {
        var lastMessage = string.Empty;
        return new StatusBitEditorPanelViewModel(
            StatusBitCatalog.All,
            writer ?? ((_, _) => Task.FromResult(GameApiDispatchStatus.Completed)),
            canExecute ?? (() => true),
            message => lastMessage = message);
    }

    private static StatusBitEditorPanelViewModel CreateViewModel(
        out Func<string> lastMessageAccessor,
        Func<StatusBitDefinition, bool, Task<GameApiDispatchStatus>>? writer = null,
        Func<bool>? canExecute = null)
    {
        var lastMessage = string.Empty;
        lastMessageAccessor = () => lastMessage;
        return new StatusBitEditorPanelViewModel(
            StatusBitCatalog.All,
            writer ?? ((_, _) => Task.FromResult(GameApiDispatchStatus.Completed)),
            canExecute ?? (() => true),
            message => lastMessage = message);
    }
}
