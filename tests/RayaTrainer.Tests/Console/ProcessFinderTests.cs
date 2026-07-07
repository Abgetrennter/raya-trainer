namespace RayaTrainer.Tests.Console;

using Ra3LuaConsole.Injector;
using Xunit;

public class ProcessFinderTests
{
    [Fact]
    public void FindByName_MatchesCaseInsensitive()
    {
        var provider = new FakeProcessProvider(
            new ProcessInfo(100, "ra3_1.12.game.exe"),
            new ProcessInfo(200, "notepad.exe"));
        var finder = new ProcessFinder(provider);

        var pid = finder.FindByName("RA3_1.12.GAME.EXE");

        Assert.Equal(100, pid);
    }

    [Fact]
    public void FindByName_WhenNotFound_ReturnsZero()
    {
        var provider = new FakeProcessProvider(new ProcessInfo(1, "other.exe"));
        var finder = new ProcessFinder(provider);

        var pid = finder.FindByName("ra3_1.12.game.exe");

        Assert.Equal(0, pid);
    }

    [Fact]
    public void FindByName_WhenMultipleMatches_ReturnsFirst()
    {
        var provider = new FakeProcessProvider(
            new ProcessInfo(100, "ra3_1.12.game.exe"),
            new ProcessInfo(200, "ra3_1.12.game.exe"));
        var finder = new ProcessFinder(provider);

        var pid = finder.FindByName("ra3_1.12.game.exe");

        Assert.Equal(100, pid);
    }

    [Fact]
    public void FindByName_TreatsGameSuffixLikeExecutableSuffix()
    {
        var provider = new FakeProcessProvider(new ProcessInfo(100, "ra3_1.12"));
        var finder = new ProcessFinder(provider);

        var pid = finder.FindByName("ra3_1.12.game");

        Assert.Equal(100, pid);
    }

    private sealed class FakeProcessProvider : IProcessListProvider
    {
        private readonly ProcessInfo[] _procs;
        public FakeProcessProvider(params ProcessInfo[] procs) => _procs = procs;
        public IReadOnlyList<ProcessInfo> GetProcesses() => _procs;
    }
}
