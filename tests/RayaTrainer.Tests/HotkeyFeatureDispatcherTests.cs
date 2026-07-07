using RayaTrainer.Core.Hotkeys;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class HotkeyFeatureDispatcherTests
{
    [Fact]
    public void TryDispatchIgnoresKeysWhenHotkeysAreDisabled()
    {
        var dispatcher = new HotkeyFeatureDispatcher();
        var invoked = 0;
        dispatcher.Update(
            [new HotkeyActionBinding(
                HotkeyGesture.Parse("P"),
                () => invoked++)],
            enabled: false);

        var handled = dispatcher.TryDispatch(0x50, HotkeyModifiers.None);

        Assert.False(handled);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void TryDispatchTriggersMatchingFeatureOncePerKeyPress()
    {
        var dispatcher = new HotkeyFeatureDispatcher();
        var invoked = 0;
        dispatcher.Update(
            [new HotkeyActionBinding(
                HotkeyGesture.Parse("P"),
                () => invoked++)],
            enabled: true);

        Assert.True(dispatcher.TryDispatch(0x50, HotkeyModifiers.None));
        Assert.True(dispatcher.TryDispatch(0x50, HotkeyModifiers.None));
        dispatcher.Release(0x50);
        Assert.True(dispatcher.TryDispatch(0x50, HotkeyModifiers.None));

        Assert.Equal(2, invoked);
    }

    [Fact]
    public void TryDispatchThrottlesRepeatableActionsByRepeatInterval()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var dispatcher = new HotkeyFeatureDispatcher(TimeSpan.FromMilliseconds(200), () => now);
        var invoked = 0;
        dispatcher.Update(
            [new HotkeyActionBinding(
                HotkeyGesture.Parse("Home"),
                () => invoked++,
                AllowRepeat: true)],
            enabled: true);

        Assert.True(dispatcher.TryDispatch(0x24, HotkeyModifiers.None));
        now = now.AddMilliseconds(199);
        Assert.True(dispatcher.TryDispatch(0x24, HotkeyModifiers.None));
        Assert.Equal(1, invoked);

        now = now.AddMilliseconds(1);
        Assert.True(dispatcher.TryDispatch(0x24, HotkeyModifiers.None));

        Assert.Equal(2, invoked);
    }

    [Fact]
    public void TryDispatchDoesNotThrottleFreshKeyPressAfterRelease()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var dispatcher = new HotkeyFeatureDispatcher(TimeSpan.FromMilliseconds(200), () => now);
        var invoked = 0;
        dispatcher.Update(
            [new HotkeyActionBinding(
                HotkeyGesture.Parse("Home"),
                () => invoked++,
                AllowRepeat: true)],
            enabled: true);

        Assert.True(dispatcher.TryDispatch(0x24, HotkeyModifiers.None));
        dispatcher.Release(0x24);
        now = now.AddMilliseconds(50);
        Assert.True(dispatcher.TryDispatch(0x24, HotkeyModifiers.None));

        Assert.Equal(2, invoked);
    }

    [Fact]
    public void TryDispatchCanTriggerMultipleSourceTrainerActionsOnSameHotkey()
    {
        var dispatcher = new HotkeyFeatureDispatcher();
        var invoked = new List<string>();
        dispatcher.Update(
            [
                new HotkeyActionBinding(
                    HotkeyGesture.Parse("/"),
                    () => invoked.Add("id")),
                new HotkeyActionBinding(
                    HotkeyGesture.Parse("/"),
                    () => invoked.Add("danger")),
                new HotkeyActionBinding(
                    HotkeyGesture.Parse("/"),
                    () => invoked.Add("ore"))
            ],
            enabled: true);

        var handled = dispatcher.TryDispatch(0xBF, HotkeyModifiers.None);

        Assert.True(handled);
        Assert.Equal(["id", "danger", "ore"], invoked);
    }

    [Fact]
    public void TryDispatchIgnoresActionWhenCanExecuteReturnsFalse()
    {
        var dispatcher = new HotkeyFeatureDispatcher();
        var invoked = 0;
        dispatcher.Update(
            [new HotkeyActionBinding(
                HotkeyGesture.Parse("Insert"),
                () => invoked++,
                () => false)],
            enabled: true);

        var handled = dispatcher.TryDispatch(0x2D, HotkeyModifiers.None);

        Assert.False(handled);
        Assert.Equal(0, invoked);
    }
}
