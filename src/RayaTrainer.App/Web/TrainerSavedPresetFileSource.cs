using System.IO;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.Web;

public sealed class TrainerSavedPresetFileSource : ITrainerSavedPresetSource
{
    private readonly string _baseDirectory;
    private readonly string _currentSettingsPath;

    public TrainerSavedPresetFileSource(string? baseDirectory = null, string? currentSettingsPath = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _currentSettingsPath = Path.GetFullPath(currentSettingsPath ?? TrainerAppSettingsStore.DefaultPath(_baseDirectory));
    }

    public IReadOnlyList<TrainerAppSettings> LoadSavedSettings()
    {
        var settings = new List<TrainerAppSettings>();
        foreach (var path in EnumerateCandidatePaths())
        {
            try
            {
                settings.Add(new TrainerAppSettingsStore(path).Load());
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return settings;
    }

    private IEnumerable<string> EnumerateCandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            _currentSettingsPath
        };

        foreach (var directory in EnumerateCandidateDirectories())
        {
            var path = Path.GetFullPath(Path.Combine(directory, TrainerAppSettingsStore.SettingsFileName));
            if (seen.Add(path) && File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private IEnumerable<string> EnumerateCandidateDirectories()
    {
        var appDirectory = new DirectoryInfo(_baseDirectory);
        var repoRoot = FindRepositoryRoot(appDirectory);
        if (repoRoot is null)
        {
            yield break;
        }

        var appProjectDirectory = Path.Combine(repoRoot.FullName, "src", "RayaTrainer.App");
        var artifactDirectory = Path.Combine(repoRoot.FullName, "artifacts");
        foreach (var directory in EnumerateExistingDirectories(appProjectDirectory, artifactDirectory))
        {
            yield return directory;
        }
    }

    private static DirectoryInfo? FindRepositoryRoot(DirectoryInfo? directory)
    {
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateExistingDirectories(params string[] roots)
    {
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            yield return root;
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                yield return directory;
            }
        }
    }
}
