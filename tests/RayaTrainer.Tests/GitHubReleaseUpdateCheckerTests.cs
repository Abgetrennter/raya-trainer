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
                  "tag_name": "v0.1.14",
                  "name": "RA3 Trainer v0.1.14",
                  "html_url": "https://github.com/Abgetrennter/Ra3-trainer-refine/releases/tag/v0.1.14",
                  "published_at": "2026-06-10T01:59:54Z",
                  "assets": [
                    {
                      "name": "RA3修改器-v0.1.14-推荐版-无需安装NET.zip",
                      "browser_download_url": "https://example.test/recommended.zip"
                    }
                  ]
                }
                """)
        });
        var checker = new GitHubReleaseUpdateChecker(new HttpClient(handler));

        var result = await checker.CheckLatestStableReleaseAsync("v0.1.13");

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("v0.1.14", result.LatestVersion);
        Assert.Equal("RA3 Trainer v0.1.14", result.ReleaseName);
        Assert.Equal("https://github.com/Abgetrennter/Ra3-trainer-refine/releases/tag/v0.1.14", result.ReleaseUrl);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 1, 59, 54, TimeSpan.Zero), result.PublishedAt);
        Assert.Contains(result.Assets, asset => asset.Name.Contains("推荐版", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckLatestStableReleaseAsyncSendsGitHubHeaders()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"v0.1.13","html_url":"https://example.test/release"}""")
        });
        var checker = new GitHubReleaseUpdateChecker(new HttpClient(handler));

        await checker.CheckLatestStableReleaseAsync("v0.1.13");

        Assert.Equal("application/vnd.github+json", handler.Request!.Headers.Accept.Single().MediaType);
        Assert.Equal("2026-03-10", handler.Request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Contains("RayaTrainer", handler.Request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Equal("https://api.github.com/repos/Abgetrennter/Ra3-trainer-refine/releases/latest", handler.Request.RequestUri!.AbsoluteUri);
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
