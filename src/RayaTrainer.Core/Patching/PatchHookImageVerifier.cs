using System.Globalization;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Patching;

public sealed record PatchHookImageVerificationResult(
    string Address,
    string SectionTitle,
    int Rva,
    int RawOffset,
    string SectionName,
    byte[] ExpectedBytes,
    byte[] ImageBytes,
    bool Matches);

public static class PatchHookImageVerifier
{
    public static IReadOnlyList<PatchHookImageVerificationResult> Verify(PatchManifest manifest, PeImage image)
    {
        return manifest.Hooks
            .Select(hook =>
            {
                var expectedBytes = OriginalByteParser.Parse(hook.OriginalAssembly);
                var rva = ParseRva(hook.Address);
                var mapping = image.MapRva(rva, expectedBytes.Length);
                var imageBytes = image.ReadRva(rva, expectedBytes.Length);
                return new PatchHookImageVerificationResult(
                    hook.Address,
                    hook.SectionTitle,
                    rva,
                    mapping.RawOffset,
                    mapping.Section.Name,
                    expectedBytes,
                    imageBytes,
                    imageBytes.SequenceEqual(expectedBytes));
            })
            .ToArray();
    }

    public static IReadOnlyList<PatchHookImageVerificationResult> Verify(
        PatchManifest manifest,
        PeImage image,
        Ra3VersionProfile profile)
    {
        return manifest.Hooks
            .Select(hook => new
            {
                Hook = hook,
                Key = HookKey(hook)
            })
            .Where(item =>
                profile.Hooks.TryGetValue(item.Key, out var address)
                && address.Status == AddressSupportStatus.Verified
                && address.Rva is not null)
            .Select(item =>
            {
                var profileHook = profile.Hooks[item.Key];
                var rva = profileHook.Rva!.Value;
                var expectedBytes = profileHook.ExpectedBytes?.ToArray()
                    ?? OriginalByteParser.Parse(item.Hook.OriginalAssembly);
                var mapping = image.MapRva(rva, expectedBytes.Length);
                var imageBytes = image.ReadRva(rva, expectedBytes.Length);
                return new PatchHookImageVerificationResult(
                    $"{profile.ProcessName}+{rva:X}",
                    item.Hook.SectionTitle,
                    rva,
                    mapping.RawOffset,
                    mapping.Section.Name,
                    expectedBytes,
                    imageBytes,
                    imageBytes.SequenceEqual(expectedBytes));
            })
            .ToArray();
    }

    private static int ParseRva(string address)
    {
        var plusIndex = address.LastIndexOf('+');
        if (plusIndex < 0 || plusIndex == address.Length - 1)
        {
            throw new InvalidOperationException($"Unsupported hook address '{address}'.");
        }

        return int.Parse(address[(plusIndex + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string HookKey(PatchHook hook)
    {
        return string.IsNullOrWhiteSpace(hook.ReturnLabel)
            ? hook.Address
            : hook.ReturnLabel;
    }
}
