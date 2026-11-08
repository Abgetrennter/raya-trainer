using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RayaTrainer.App.Services;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckLatestStableReleaseAsync(string currentVersion, CancellationToken cancellationToken = default);
}

public sealed class GitHubReleaseUpdateChecker : IUpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Abgetrennter/raya-trainer/releases/latest";
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateChecker()
        : this(new HttpClient())
    {
    }

    public GitHubReleaseUpdateChecker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UpdateCheckResult> CheckLatestStableReleaseAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateRequest();
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failure(currentVersion, CreateFailureMessage(response.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                return UpdateCheckResult.Failure(currentVersion, "检查更新失败：GitHub Release 响应缺少版本信息。");
            }

            var assets = (release.Assets ?? [])
                .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                .Select(asset => new UpdateReleaseAsset(asset.Name!, asset.BrowserDownloadUrl!))
                .ToArray();

            return UpdateCheckResult.Success(
                currentVersion,
                release.TagName,
                release.Name ?? release.TagName,
                release.HtmlUrl,
                release.PublishedAt,
                assets);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failure(currentVersion, "检查更新失败：GitHub 请求超时。");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failure(currentVersion, $"检查更新失败：网络请求失败，{ex.Message}");
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failure(currentVersion, "检查更新失败：GitHub Release 响应格式无法解析。");
        }
    }

    private static HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.UserAgent.ParseAdd("RayaTrainer/1.0");
        return request;
    }

    private static string CreateFailureMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => "检查更新失败：GitHub 请求受限，请稍后再试。",
            HttpStatusCode.NotFound => "检查更新失败：未找到 GitHub Release。",
            _ => $"检查更新失败：GitHub 返回 HTTP {(int)statusCode}。"
        };
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("assets")]
        public GitHubReleaseAssetResponse[]? Assets { get; init; }
    }

    private sealed class GitHubReleaseAssetResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}
