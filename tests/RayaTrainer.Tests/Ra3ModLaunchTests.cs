using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class Ra3ModLaunchTests
{
    [Fact]
    public void ModCatalogScansCustomModsRootSkudefs()
    {
        var modsRoot = CreateTempDirectory();
        var modDirectory = Path.Combine(modsRoot, "Custom Mod");
        Directory.CreateDirectory(modDirectory);
        var skudefPath = Path.Combine(modDirectory, "CustomMod_1.2.skudef");
        File.WriteAllText(skudefPath, """
            mod-game 1.12
            add-big Data\CustomMod.big
            """);
        Directory.CreateDirectory(Path.Combine(modsRoot, "EmptyFolder"));

        var mods = Ra3ModCatalog.Load(modsRoot);

        var mod = Assert.Single(mods);
        Assert.Equal("CustomMod", mod.Name);
        Assert.Equal("1.2", mod.Version);
        Assert.Equal("CustomMod 1.2", mod.DisplayName);
        Assert.Equal("1.12", mod.GameVersion);
        Assert.Equal(skudefPath, mod.SkudefPath);
    }

    [Fact]
    public void DirectLaunchPlannerBuildsCommandFromSelectedMod()
    {
        var gameRoot = Path.Combine(CreateTempDirectory(), "Game Root");
        var dataDirectory = Path.Combine(gameRoot, "Data");
        Directory.CreateDirectory(dataDirectory);
        var gameExe = Path.Combine(dataDirectory, "ra3_1.12.game");
        File.WriteAllText(gameExe, string.Empty);
        var config = Path.Combine(gameRoot, "ra3_english_1.12.skudef");
        File.WriteAllText(config, "set-exe Data\\ra3_1.12.game");

        var modsRoot = CreateTempDirectory();
        var modDirectory = Path.Combine(modsRoot, "Custom Mod");
        Directory.CreateDirectory(modDirectory);
        var modSkudef = Path.Combine(modDirectory, "CustomMod_1.2.skudef");
        File.WriteAllText(modSkudef, "mod-game 1.12");

        var plan = Ra3DirectLaunchPlanner.Create(gameRoot, modSkudef, "-win -xres 1280");

        Assert.Equal(gameExe, plan.ExecutablePath);
        Assert.Equal(gameRoot, plan.WorkingDirectory);
        Assert.Equal(
            $"-win -xres 1280 -modConfig \"{modSkudef}\" -config \"{config}\"",
            plan.Arguments);
        Assert.Equal(
            $"\"{gameExe}\" -win -xres 1280 -modConfig \"{modSkudef}\" -config \"{config}\"",
            plan.CommandLine);
    }

    [Fact]
    public void DirectLaunchPlannerSelectsNewestConfigWhenModHasNoVersion()
    {
        var gameRoot = CreateTempDirectory();
        var dataDirectory = Path.Combine(gameRoot, "Data");
        Directory.CreateDirectory(dataDirectory);
        var gameExe = Path.Combine(dataDirectory, "ra3_1.12.game");
        File.WriteAllText(gameExe, string.Empty);
        File.WriteAllText(Path.Combine(gameRoot, "ra3_english_1.11.skudef"), "set-exe Data\\old.game");
        var newestConfig = Path.Combine(gameRoot, "ra3_english_1.12.skudef");
        File.WriteAllText(newestConfig, "set-exe Data\\ra3_1.12.game");

        var modsRoot = CreateTempDirectory();
        var modDirectory = Path.Combine(modsRoot, "Custom Mod");
        Directory.CreateDirectory(modDirectory);
        var modSkudef = Path.Combine(modDirectory, "CustomMod_1.2.skudef");
        File.WriteAllText(modSkudef, "add-big Data\\CustomMod.big");

        var plan = Ra3DirectLaunchPlanner.Create(gameRoot, modSkudef, string.Empty);

        Assert.Equal(gameExe, plan.ExecutablePath);
        Assert.EndsWith($"-config \"{newestConfig}\"", plan.Arguments);
    }

    [Fact]
    public void DirectLaunchPlannerBuildsCommandWithoutModConfigWhenNoModIsSelected()
    {
        var gameRoot = CreateTempDirectory();
        var dataDirectory = Path.Combine(gameRoot, "Data");
        Directory.CreateDirectory(dataDirectory);
        var gameExe = Path.Combine(dataDirectory, "ra3_1.12.game");
        File.WriteAllText(gameExe, string.Empty);
        var config = Path.Combine(gameRoot, "ra3_english_1.12.skudef");
        File.WriteAllText(config, "set-exe Data\\ra3_1.12.game");

        var plan = Ra3DirectLaunchPlanner.Create(gameRoot, string.Empty, "-win");

        Assert.Equal(gameExe, plan.ExecutablePath);
        Assert.Equal(gameRoot, plan.WorkingDirectory);
        Assert.Equal($"-win -config \"{config}\"", plan.Arguments);
        Assert.DoesNotContain("-modConfig", plan.Arguments);
        Assert.Equal($"\"{gameExe}\" -win -config \"{config}\"", plan.CommandLine);
    }

    [Fact]
    public void GameLauncherStartInfoUsesExplicitWorkingDirectory()
    {
        var directory = CreateTempDirectory();
        var executable = Path.Combine(directory, "ra3_1.12.game");
        File.WriteAllText(executable, string.Empty);
        var workingDirectory = Path.Combine(directory, "GameRoot");
        Directory.CreateDirectory(workingDirectory);

        var startInfo = GameLauncher.CreateStartInfo(executable, "-win", workingDirectory);

        Assert.Equal(executable, startInfo.FileName);
        Assert.Equal("-win", startInfo.Arguments);
        Assert.Equal(workingDirectory, startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void GameLauncherRawProcessRequestKeepsCommandLineAndNormalizesWorkingDirectory()
    {
        var workingDirectory = CreateTempDirectory();

        var request = GameLauncher.CreateRawProcessLaunchRequest(
            "  \"C:\\Games\\RA3\\Data\\ra3_1.12.game\" -config \"C:\\Games\\RA3\\RA3_english_1.12.SkuDef\"  ",
            workingDirectory);

        Assert.Equal(
            "\"C:\\Games\\RA3\\Data\\ra3_1.12.game\" -config \"C:\\Games\\RA3\\RA3_english_1.12.SkuDef\"",
            request.CommandLine);
        Assert.Equal(Path.GetFullPath(workingDirectory), request.WorkingDirectory);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
