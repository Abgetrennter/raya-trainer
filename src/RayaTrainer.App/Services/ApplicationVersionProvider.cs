using System.Reflection;

namespace RayaTrainer.App.Services;

public interface IApplicationVersionProvider
{
    string CurrentVersion { get; }
}

public sealed class ApplicationVersionProvider : IApplicationVersionProvider
{
    public string CurrentVersion { get; } = ResolveCurrentVersion();

    private static string ResolveCurrentVersion()
    {
        var assembly = typeof(ApplicationVersionProvider).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return TrimBuildMetadata(informationalVersion);
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0-local";
    }

    private static string TrimBuildMetadata(string version)
    {
        var normalized = version.Trim();
        var metadataIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex >= 0 ? normalized[..metadataIndex] : normalized;
    }
}
