using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

public static class Ra3VersionProfileRegistry
{
    public static Ra3VersionProfile Ra3112 { get; } = Ra3_1_12_Profile.Create();

    public static Ra3VersionProfile Ra3113 { get; } = Ra3_1_13_Profile.Create();

    public static Ra3VersionProfile Uprising10 { get; } = Ra3_Uprising_Profile.Create10();

    public static Ra3VersionProfile Uprising11 { get; } = Ra3_Uprising_Profile.Create11();

    public static IReadOnlyList<Ra3VersionProfile> Profiles { get; } =
        [Ra3112, Ra3113, Uprising10, Uprising11];

    public static IReadOnlyList<Ra3VersionProfile> InstallableProfiles { get; } =
        Profiles.Where(profile => profile.IsPatchInstallable).ToArray();

    public static Ra3VersionProfile? FindByFileVersion(string fileVersion)
    {
        return Profiles.FirstOrDefault(profile => profile.MatchesFileVersion(fileVersion));
    }

    public static Ra3VersionProfile? FindById(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        return Profiles.FirstOrDefault(profile => profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    }

    public static Ra3VersionProfile? ResolveTargetProfile(TrainerTarget target)
    {
        var profile = FindById(target.VersionProfileId);
        if (profile is not null || !string.IsNullOrWhiteSpace(target.VersionProfileId))
        {
            return profile;
        }

        profile = FindByFileVersion(target.FileVersion);
        if (profile?.MatchesProcessName(target.ProcessName) == true)
        {
            return profile;
        }

        return Ra3112.MatchesProcessName(target.ProcessName) ? Ra3112 : null;
    }

    public static Ra3VersionProfile? FindRecognizedProfile(TrainerProcessCandidate candidate)
    {
        var profile = FindByFileVersion(candidate.FileVersion);
        if (profile is null)
        {
            return null;
        }

        return profile.MatchesProcessName(candidate.ModuleName) || profile.MatchesProcessName(candidate.ProcessName)
            ? profile
            : null;
    }

    public static Ra3VersionProfile? FindInstallableProfile(TrainerProcessCandidate candidate)
    {
        var profile = FindRecognizedProfile(candidate);
        if (profile?.IsPatchInstallable != true)
        {
            return null;
        }

        return profile;
    }
}
