using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class Ra3LaunchArgumentOptionsTests
{
    [Fact]
    public void ParseRecognizesStructuredLauncherArguments()
    {
        var options = Ra3LaunchArgumentOptions.Parse("-ui -win -xres 1280 -yres 720 -xpos 12 -ypos 34 -noaudio -noAudioMusic -modConfig \"C:\\Mods\\Demo.skudef\" -replayGame \"C:\\Replays\\demo.RA3Replay\" -custom \"with space\"");

        Assert.True(options.UseLauncherUi);
        Assert.True(options.Windowed);
        Assert.False(options.Fullscreen);
        Assert.Equal("1280", options.ResolutionX);
        Assert.Equal("720", options.ResolutionY);
        Assert.Equal("12", options.WindowPositionX);
        Assert.Equal("34", options.WindowPositionY);
        Assert.True(options.NoAudio);
        Assert.True(options.NoAudioMusic);
        Assert.Equal("C:\\Mods\\Demo.skudef", options.ModConfigPath);
        Assert.Equal("C:\\Replays\\demo.RA3Replay", options.ReplayGamePath);
        Assert.Equal("-custom \"with space\"", options.ExtraArguments);
    }

    [Fact]
    public void ToCommandLineOmitsLauncherUiWhenUnchecked()
    {
        var options = new Ra3LaunchArgumentOptions(
            UseLauncherUi: false,
            Windowed: true,
            Fullscreen: false,
            ResolutionX: "1600",
            ResolutionY: "900",
            WindowPositionX: "",
            WindowPositionY: "",
            NoAudio: false,
            NoAudioMusic: false,
            ModConfigPath: "",
            ReplayGamePath: "",
            ExtraArguments: "-custom");

        Assert.Equal("-win -xres 1600 -yres 900 -custom", options.ToCommandLine());
    }

    [Fact]
    public void ToCommandLineOmitsWindowSizeAndPositionWhenWindowedIsUnchecked()
    {
        var options = new Ra3LaunchArgumentOptions(
            UseLauncherUi: true,
            Windowed: false,
            Fullscreen: false,
            ResolutionX: "1600",
            ResolutionY: "900",
            WindowPositionX: "12",
            WindowPositionY: "34",
            NoAudio: false,
            NoAudioMusic: false,
            ModConfigPath: "",
            ReplayGamePath: "",
            ExtraArguments: "-custom");

        Assert.Equal("-ui -custom", options.ToCommandLine());
    }

    [Fact]
    public void BorderlessFullscreenKeepsWindowedAndFullscreenTogether()
    {
        var options = Ra3LaunchArgumentOptions.Parse("-win -fullscreen -xres 1920 -yres 1080");

        Assert.True(options.Windowed);
        Assert.True(options.Fullscreen);
        Assert.Equal("-win -fullscreen -xres 1920 -yres 1080", options.ToCommandLine());
    }

    [Fact]
    public void ToDirectGameArgumentsOmitsLauncherUiAndModConfig()
    {
        var options = Ra3LaunchArgumentOptions.Parse("-ui -win -modConfig \"C:\\Mods\\Demo.skudef\" -noaudio -noAudioMusic -replayGame \"C:\\Replays\\demo.RA3Replay\" -custom");

        Assert.Equal("-win -noaudio -noAudioMusic -replayGame \"C:\\Replays\\demo.RA3Replay\" -custom", options.ToDirectGameArguments());
    }

    [Fact]
    public void ToCommandLineQuotesModAndReplayPaths()
    {
        var options = new Ra3LaunchArgumentOptions(
            UseLauncherUi: true,
            Windowed: false,
            Fullscreen: false,
            ResolutionX: "",
            ResolutionY: "",
            WindowPositionX: "",
            WindowPositionY: "",
            NoAudio: false,
            NoAudioMusic: false,
            ModConfigPath: "C:\\Mods\\Demo.skudef",
            ReplayGamePath: "C:\\Replays\\demo.RA3Replay",
            ExtraArguments: "");

        Assert.Equal("-ui -modConfig \"C:\\Mods\\Demo.skudef\" -replayGame \"C:\\Replays\\demo.RA3Replay\"", options.ToCommandLine());
    }
}
