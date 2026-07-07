using System.Diagnostics;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class Ra3VersionProfileImageVerificationTests
{
    [Fact]
    public void Ra3113ProfileVerifiesEveryCurrentManifestHook()
    {
        var manifest = TestAssets.LoadManifest();
        var profile = Ra3VersionProfileRegistry.Ra3113;

        var unsupportedHooks = manifest.PatchManifest.Hooks
            .Where(hook => hook.SupportsProfile(profile.Id))
            .Select(hook => string.IsNullOrWhiteSpace(hook.ReturnLabel) ? hook.Address : hook.ReturnLabel)
            .Where(key =>
                !profile.Hooks.TryGetValue(key, out var address)
                || address.Status != AddressSupportStatus.Verified
                || address.Rva is null)
            .ToArray();

        Assert.Equal(TestAssets.CurrentRa3113HookCount,
            manifest.PatchManifest.Hooks.Count(hook => hook.SupportsProfile(profile.Id)));
        Assert.Empty(unsupportedHooks);
    }

    [Theory]
    [InlineData(
        @"E:\SteamLibrary\steamapps\common\Command and Conquer Red Alert 3\Data\ra3_1.13.game",
        "1.13.0.0",
        "ra3_1.13")]
    [InlineData(
        @"E:\SteamLibrary\steamapps\common\Command and Conquer Red Alert 3 Uprising\Data\ra3ep1_1.0.game",
        "1.0.3313.38400",
        "ra3_uprising_1.0")]
    [InlineData(
        @"E:\SteamLibrary\steamapps\common\Command and Conquer Red Alert 3 Uprising\Data\ra3ep1_1.1.game",
        "1.01.0.0",
        "ra3_uprising_1.1")]
    public void LocalSampleImageMatchesVerifiedProfileHookBytes(
        string imagePath,
        string expectedFileVersion,
        string profileId)
    {
        if (!File.Exists(imagePath))
        {
            return;
        }

        var fileVersion = FileVersionInfo.GetVersionInfo(imagePath).FileVersion?.Trim();
        Assert.Equal(expectedFileVersion, fileVersion);
        var profile = Ra3VersionProfileRegistry.FindById(profileId);
        Assert.NotNull(profile);

        var results = PatchHookImageVerifier.Verify(
            TestAssets.LoadManifest().PatchManifest,
            PeImage.Load(imagePath),
            profile);
        var mismatches = results
            .Where(result => !result.Matches)
            .Select(result =>
                $"{result.SectionTitle} {result.Address} expected={FormatBytes(result.ExpectedBytes)} actual={FormatBytes(result.ImageBytes)}")
            .ToArray();

        Assert.True(results.Count > 0, "The profile must contain at least one verified hook before image verification is meaningful.");
        Assert.True(mismatches.Length == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static string FormatBytes(IEnumerable<byte> bytes)
    {
        return string.Join(" ", bytes.Select(value => value.ToString("X2")));
    }
}
