using System.Diagnostics;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Runtime;

public sealed class TrainerProcessLocator
{
    private readonly Func<IReadOnlyList<TrainerProcessCandidate>> _snapshotProcesses;

    public TrainerProcessLocator()
        : this(SnapshotProcesses)
    {
    }

    public TrainerProcessLocator(Func<IReadOnlyList<TrainerProcessCandidate>> snapshotProcesses)
    {
        _snapshotProcesses = snapshotProcesses;
    }

    public IReadOnlyList<TrainerProcessCandidate> Snapshot()
    {
        return _snapshotProcesses();
    }

    public IReadOnlyList<DetectedRa3Target> DetectAll()
    {
        return Ra3VersionDetector.DetectAll(Snapshot());
    }

    public TargetSelectionResult SelectDefault()
    {
        return Ra3VersionDetector.SelectDefault(DetectAll());
    }

    public TrainerTarget? Find(string processNameOrPath)
    {
        var candidates = Snapshot();
        var candidate = candidates.FirstOrDefault(candidate => Matches(candidate, processNameOrPath));
        if (candidate is null)
        {
            return null;
        }

        var recognizedProfile = Ra3VersionProfileRegistry.FindRecognizedProfile(candidate);
        var installableProfile = recognizedProfile?.IsPatchInstallable == true
            ? recognizedProfile
            : null;

        return new TrainerTarget(
            candidate.ModuleName,
            candidate.ModuleBase,
            candidate.Is32Bit,
            installableProfile is not null,
            candidate.ProcessId,
            candidate.ModulePath,
            candidate.FileVersion,
            recognizedProfile?.Id);
    }

    private static bool Matches(TrainerProcessCandidate candidate, string processNameOrPath)
    {
        return TrainerProcessName.Matches(candidate.ProcessName, processNameOrPath)
            || TrainerProcessName.Matches(candidate.ModuleName, processNameOrPath)
            || ModulePathMatches(candidate.ModulePath, processNameOrPath);
    }

    private static bool ModulePathMatches(string modulePath, string processNameOrPath)
    {
        return Path.GetFullPath(modulePath)
            .Equals(Path.GetFullPath(processNameOrPath), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<TrainerProcessCandidate> SnapshotProcesses()
    {
        var candidates = new List<TrainerProcessCandidate>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var module = process.MainModule;
                if (module is null)
                {
                    continue;
                }

                candidates.Add(new TrainerProcessCandidate(
                    process.Id,
                    process.ProcessName,
                    module.ModuleName,
                    module.FileName,
                    module.BaseAddress,
                    IsProcess32Bit(process),
                    module.FileVersionInfo.FileVersion ?? string.Empty));
            }
            catch
            {
                // Some system processes deny module access. They are not valid RA3 targets.
            }
        }

        return candidates;
    }

    private static bool IsProcess32Bit(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return true;
        }

        return IsWow64Process(process.Handle, out var isWow64) && isWow64;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(nint process, out bool wow64Process);
}
