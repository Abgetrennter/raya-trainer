namespace RayaTrainer.App.Services;

public sealed record UpdateReleaseAsset(string Name, string DownloadUrl);

public sealed record UpdateCheckResult(
    bool IsSuccessful,
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseName,
    string ReleaseUrl,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<UpdateReleaseAsset> Assets,
    string Message)
{
    public static UpdateCheckResult Success(
        string currentVersion,
        string latestVersion,
        string releaseName,
        string releaseUrl,
        DateTimeOffset? publishedAt,
        IReadOnlyList<UpdateReleaseAsset> assets)
    {
        var isUpdateAvailable = ReleaseVersionComparer.IsNewer(latestVersion, currentVersion);
        var message = isUpdateAvailable
            ? $"发现新版 {latestVersion}。"
            : $"当前已是最新版本 {currentVersion}。";
        return new UpdateCheckResult(
            true,
            isUpdateAvailable,
            currentVersion,
            latestVersion,
            releaseName,
            releaseUrl,
            publishedAt,
            assets,
            message);
    }

    public static UpdateCheckResult Failure(string currentVersion, string message)
    {
        return new UpdateCheckResult(
            false,
            false,
            currentVersion,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            Array.Empty<UpdateReleaseAsset>(),
            message);
    }
}
