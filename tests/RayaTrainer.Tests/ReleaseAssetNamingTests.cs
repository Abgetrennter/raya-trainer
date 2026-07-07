using Xunit;

namespace RayaTrainer.Tests;

public sealed class ReleaseAssetNamingTests
{
    [Fact]
    public void ReleaseScriptsUseAsciiPackageNamesForGitHubAssets()
    {
        var repoRoot = FindRepoRoot();
        var publish = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish.ps1"));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("RayaTrainer-v$version-win-x86-self-contained.zip", publish, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer-v$version-win-x86-framework-dependent.zip", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("RA3修改器-v$version-推荐版-无需安装NET.zip", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("RA3修改器-v$version-轻量版-需安装NET8.zip", publish, StringComparison.Ordinal);

        Assert.Contains("RayaTrainer-$tag-win-x86-$($package.Name).zip", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("RA3修改器-$tag-推荐版-无需安装NET.zip", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("RA3修改器-$tag-轻量版-需安装NET8.zip", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfContainedReleaseKeepsAspNetCoreFrameworkBundled()
    {
        var repoRoot = FindRepoRoot();
        var project = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.App", "RayaTrainer.App.csproj"));
        var publish = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish.ps1"));
        var selfContained = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-self-contained.ps1"));

        Assert.Contains("<FrameworkReference Include=\"Microsoft.AspNetCore.App\" />", project, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", publish, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSelfContained=true", publish, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", selfContained, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSelfContained=true", selfContained, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
