using RayaTrainer.Core.Hotkeys;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+F1", 0x70, HotkeyModifiers.Control, "Ctrl+F1")]
    [InlineData("P", 0x50, HotkeyModifiers.None, "P")]
    [InlineData("-", 0xBD, HotkeyModifiers.None, "-")]
    [InlineData("=", 0xBB, HotkeyModifiers.None, "=")]
    [InlineData("Page Up", 0x21, HotkeyModifiers.None, "Page Up")]
    [InlineData("Page Down", 0x22, HotkeyModifiers.None, "Page Down")]
    [InlineData("Home", 0x24, HotkeyModifiers.None, "Home")]
    [InlineData("Insert", 0x2D, HotkeyModifiers.None, "Insert")]
    [InlineData("[", 0xDB, HotkeyModifiers.None, "[")]
    [InlineData("]", 0xDD, HotkeyModifiers.None, "]")]
    [InlineData("\\", 0xDC, HotkeyModifiers.None, "\\")]
    [InlineData(";", 0xBA, HotkeyModifiers.None, ";")]
    [InlineData("/", 0xBF, HotkeyModifiers.None, "/")]
    [InlineData("Delete", 0x2E, HotkeyModifiers.None, "Delete")]
    [InlineData(",", 0xBC, HotkeyModifiers.None, ",")]
    [InlineData(".", 0xBE, HotkeyModifiers.None, ".")]
    [InlineData("L", 0x4C, HotkeyModifiers.None, "L")]
    [InlineData("X", 0x58, HotkeyModifiers.None, "X")]
    [InlineData("J", 0x4A, HotkeyModifiers.None, "J")]
    [InlineData("I", 0x49, HotkeyModifiers.None, "I")]
    public void TryParseSupportsSourceTrainerScreenshotKeys(
        string text,
        int expectedVirtualKey,
        HotkeyModifiers expectedModifiers,
        string expectedDisplayText)
    {
        var parsed = HotkeyGesture.TryParse(text, out var gesture);

        Assert.True(parsed);
        Assert.NotNull(gesture);
        Assert.Equal(expectedVirtualKey, gesture.VirtualKey);
        Assert.Equal(expectedModifiers, gesture.Modifiers);
        Assert.Equal(expectedDisplayText, gesture.DisplayText);
    }

    [Theory]
    [InlineData("#189", 0xBD, "-")]
    [InlineData("#187", 0xBB, "=")]
    [InlineData("#219", 0xDB, "[")]
    [InlineData("#221", 0xDD, "]")]
    [InlineData("#220", 0xDC, "\\")]
    [InlineData("#186", 0xBA, ";")]
    [InlineData("#188", 0xBC, ",")]
    [InlineData("#190", 0xBE, ".")]
    [InlineData("#191", 0xBF, "/")]
    [InlineData("#222", 0xDE, "'")]
    public void TryParseSupportsRawVirtualKeyFallbacks(
        string text,
        int expectedVirtualKey,
        string expectedDisplayText)
    {
        var parsed = HotkeyGesture.TryParse(text, out var gesture);

        Assert.True(parsed);
        Assert.NotNull(gesture);
        Assert.Equal(expectedVirtualKey, gesture.VirtualKey);
        Assert.Equal(HotkeyModifiers.None, gesture.Modifiers);
        Assert.Equal(expectedDisplayText, gesture.DisplayText);
    }

    [Theory]
    [InlineData(0x70, HotkeyModifiers.Control, "Ctrl+F1")]
    [InlineData(0x50, HotkeyModifiers.None, "P")]
    [InlineData(0xBD, HotkeyModifiers.None, "-")]
    [InlineData(0x21, HotkeyModifiers.None, "Page Up")]
    [InlineData(0x2D, HotkeyModifiers.Alt, "Alt+Insert")]
    [InlineData(0x2E, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+Delete")]
    public void FromVirtualKeyProducesSameDisplayTextAsTryParse(int virtualKey, HotkeyModifiers modifiers, string expectedDisplayText)
    {
        var gesture = HotkeyGesture.FromVirtualKey(virtualKey, modifiers);

        Assert.Equal(virtualKey, gesture.VirtualKey);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(expectedDisplayText, gesture.DisplayText);
    }
}

