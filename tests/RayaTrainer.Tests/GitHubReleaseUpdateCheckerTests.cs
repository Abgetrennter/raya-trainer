using System.Net;
using System.Net.Http;
using RayaTrainer.App.Services;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class GitHubReleaseUpdateCheckerTests
{
    [Fact]
    public async Task CheckLatestStableReleaseAsyncParsesNewerReleaseJson()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "v0.0.3",
                  "name": "RayaTrainer v0.0.3",
                  "html_url": "https://github.com/Abgetrennter/raya-trainer/releases/tag/v0.0.3",
                  "published_at": "2026-07-13T06:58:29Z",
                  "assets": [
                    {
                      "name": "RayaTrainer-v0.0.3-win-x86-self-contained.zip",
                      "browser_download_url": "https://github.com/Abgetrennter/raya-trainer/releases/download/v0.0.3/RayaTrainer-v0.0.3-win-x86-self-contained.zip"
                    }
                  ]
                }
                """)
        });
        var checker = new GitHubReleaseUpdateChecker(new HttpClient(handler));

        var result = await checker.CheckLatestStableReleaseAsync("v0.0.2");

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("v0.0.3", result.LatestVersion);
        Assert.Equal("RayaTrainer v0.0.3", result.ReleaseName);
        Assert.Equal("https://github.com/Abgetrennter/raya-trainer/releases/tag/v0.0.3", result.ReleaseUrl);
        Assert.Equal(new DateTimeOffset(2026, 7, 13, 6, 58, 29, TimeSpan.Zero), result.PublishedAt);
        Assert.Contains(result.Assets, asset => asset.Name.Contains("self-contained", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckLatestStableReleaseAsyncSendsGitHubHeaders()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"v0.0.3","html_url":"https://example.test/release"}""")
        });
        var checker = new GitHubReleaseUpdateChecker(new HttpClient(handler));

        await checker.CheckLatestStableReleaseAsync("v0.0.3");

        Assert.Equal("application/vnd.github+json", handler.Request!.Headers.Accept.Single().MediaType);
        Assert.Equal("2026-03-10", handler.Request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Contains("RayaTrainer", handler.Request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Equal("https://api.github.com/repos/Abgetrennter/raya-trainer/releases/latest", handler.Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CheckLatestStableReleaseAsyncReportsRateLimitFailure()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"message":"API rate limit exceeded"}""")
        });
        var checker = new GitHubReleaseUpdateChecker(new HttpClient(handler));

        var result = await checker.CheckLatestStableReleaseAsync("v0.1.13");

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("GitHub 请求受限", result.Message);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(_response);
        }
    }
}
