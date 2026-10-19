using System.Reflection;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

internal static class Ra3_1_12_Profile
{
    public static Ra3VersionProfile Create() =>
        PrivateVersionEvidence.TryCreate(nameof(Ra3_1_12_ProfileEvidence), nameof(Create))
        ?? PublicProfile(
            "ra3_1.12",
            "RA3 1.12",
            GameTarget.ProcessName,
            new HashSet<string>([GameTarget.ExpectedVersion], StringComparer.OrdinalIgnoreCase));

    private const string Ra3_1_12_ProfileEvidence = "Ra3_1_12_ProfileEvidence";

    private static Ra3VersionProfile PublicProfile(
        string id,
        string displayName,
        string processName,
        IReadOnlySet<string> fileVersions) =>
        Ra3PublicProfileFactory.Create(id, displayName, processName, fileVersions);
}

internal static class Ra3_1_13_Profile
{
    public static Ra3VersionProfile Create() =>
        PrivateVersionEvidence.TryCreate(nameof(Ra3_1_13_ProfileEvidence), nameof(Create))
        ?? Ra3PublicProfileFactory.Create(
            "ra3_1.13",
            "RA3 1.13",
            "ra3_1.13.game",
            new HashSet<string>(["1.13.0.0", "1.13.3444.25830"], StringComparer.OrdinalIgnoreCase));

    private const string Ra3_1_13_ProfileEvidence = "Ra3_1_13_ProfileEvidence";
}

internal static class Ra3_Uprising_Profile
{
    public static Ra3VersionProfile Create10() =>
        PrivateVersionEvidence.TryCreate(nameof(Ra3_Uprising_ProfileEvidence), nameof(Create10))
        ?? Ra3PublicProfileFactory.Create(
            "ra3_uprising_1.0",
            "RA3 Uprising 1.0",
            "ra3ep1_1.0.game",
            new HashSet<string>(["1.0.3313.38400"], StringComparer.OrdinalIgnoreCase));

    public static Ra3VersionProfile Create11() =>
        PrivateVersionEvidence.TryCreate(nameof(Ra3_Uprising_ProfileEvidence), nameof(Create11))
        ?? Ra3PublicProfileFactory.Create(
            "ra3_uprising_1.1",
            "RA3 Uprising 1.1",
            "ra3ep1_1.1.game",
            new HashSet<string>(["1.01.0.0"], StringComparer.OrdinalIgnoreCase));

    private const string Ra3_Uprising_ProfileEvidence = "Ra3_Uprising_ProfileEvidence";
}

internal static class Ra3PublicProfileFactory
{
    public static Ra3VersionProfile Create(
        string id,
        string displayName,
        string processName,
        IReadOnlySet<string> fileVersions)
    {
        return new Ra3VersionProfile
        {
            Id = id,
            DisplayName = displayName,
            ProcessName = processName,
            FileVersions = fileVersions,
            SupportsSignatureScanning = true,
            Hooks = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase),
            RemoteGlobals = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase),
            EngineFunctions = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase),
        };
    }
}

internal static class PrivateVersionEvidence
{
    public static Ra3VersionProfile? TryCreate(string typeName, string methodName)
    {
        var type = typeof(Ra3VersionProfile).Assembly.GetType(
            $"{typeof(Ra3VersionProfile).Namespace}.{typeName}",
            throwOnError: false,
            ignoreCase: false);
        var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, null) as Ra3VersionProfile;
    }
}
