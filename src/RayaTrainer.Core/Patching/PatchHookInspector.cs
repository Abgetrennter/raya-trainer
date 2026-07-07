using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Patching;

public sealed record PatchHookByteSnapshot(
    string Address,
    string SectionTitle,
    nint AbsoluteAddress,
    byte[] ExpectedBytes,
    byte[] ActualBytes,
    bool Matches);

public static class PatchHookInspector
{
    public static IReadOnlyList<PatchHookByteSnapshot> CaptureResolved(
        PatchManifest manifest,
        IProcessMemory memory,
        IReadOnlyDictionary<string, uint> resolvedAddresses,
        IReadOnlyDictionary<string, byte[]>? expectedBytesByHook = null,
        bool skipUnresolved = false)
    {
        ArgumentNullException.ThrowIfNull(resolvedAddresses);

        return manifest.Hooks
            .SelectMany(hook =>
            {
                var key = HookKey(hook);
                if (!resolvedAddresses.TryGetValue(key, out var absoluteAddress) || absoluteAddress == 0)
                {
                    if (skipUnresolved)
                    {
                        return Array.Empty<PatchHookByteSnapshot>();
                    }
                    throw new InvalidOperationException($"Resolved hook catalog is missing '{key}'.");
                }

                var expectedBytes = expectedBytesByHook is null
                    ? OriginalByteParser.Parse(hook.OriginalAssembly)
                    : RequireExpectedBytes(expectedBytesByHook, key);
                var address = unchecked((nint)absoluteAddress);
                var actualBytes = memory.ReadBytes(address, expectedBytes.Length);
                return new[]
                {
                    new PatchHookByteSnapshot(
                        key,
                        hook.SectionTitle,
                        address,
                        expectedBytes,
                        actualBytes,
                        actualBytes.SequenceEqual(expectedBytes))
                };
            })
            .ToArray();
    }

    private static byte[] RequireExpectedBytes(
        IReadOnlyDictionary<string, byte[]> expectedBytesByHook,
        string key)
    {
        if (!expectedBytesByHook.TryGetValue(key, out var bytes) || bytes.Length == 0)
        {
            throw new InvalidOperationException($"Hook byte baseline is missing '{key}'.");
        }

        return bytes.ToArray();
    }

    public static IReadOnlyList<PatchHookByteSnapshot> Capture(
        PatchManifest manifest,
        IProcessMemory memory,
        AddressResolver resolver,
        Ra3VersionProfile? profile = null)
    {
        return manifest.Hooks
            .SelectMany(hook =>
            {
                var hookSnapshot = CreateSnapshotInput(hook, profile);
                if (hookSnapshot is null)
                {
                    return Array.Empty<PatchHookByteSnapshot>();
                }

                var address = resolver.Resolve(hookSnapshot.AddressExpression);
                var actualBytes = memory.ReadBytes(address, hookSnapshot.ExpectedBytes.Length);
                return new[]
                {
                    new PatchHookByteSnapshot(
                        hookSnapshot.AddressExpression,
                        hookSnapshot.SectionTitle,
                        address,
                        hookSnapshot.ExpectedBytes,
                        actualBytes,
                        actualBytes.SequenceEqual(hookSnapshot.ExpectedBytes))
                };
            })
            .ToArray();
    }

    private static HookSnapshotInput? CreateSnapshotInput(PatchHook hook, Ra3VersionProfile? profile)
    {
        if (profile is null)
        {
            return new HookSnapshotInput(
                hook.Address,
                hook.SectionTitle,
                OriginalByteParser.Parse(hook.OriginalAssembly));
        }

        var key = HookKey(hook);
        if (!profile.Hooks.TryGetValue(key, out var profileHook)
            || profileHook.Status != AddressSupportStatus.Verified
            || profileHook.Rva is null)
        {
            return null;
        }

        return new HookSnapshotInput(
            $"{profile.ProcessName}+{profileHook.Rva.Value:X}",
            hook.SectionTitle,
            profileHook.ExpectedBytes?.ToArray() ?? OriginalByteParser.Parse(hook.OriginalAssembly));
    }

    private static string HookKey(PatchHook hook)
    {
        return string.IsNullOrWhiteSpace(hook.ReturnLabel)
            ? hook.Address
            : hook.ReturnLabel;
    }

    private sealed record HookSnapshotInput(
        string AddressExpression,
        string SectionTitle,
        byte[] ExpectedBytes);
}
