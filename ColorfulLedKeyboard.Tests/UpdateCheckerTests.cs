using ColorfulLedKeyboard.Tray;
using System.Net;

namespace ColorfulLedKeyboard.Tests;

public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task RedirectHeaderResolvesReleaseWithoutDownloadingPage()
    {
        var handler = new QueueMessageHandler(
            Response(HttpStatusCode.Found, "https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/tag/v2.5.1"));
        var checker = CreateChecker(handler);

        var result = await checker.ResolveLatestReleaseAsync();

        Assert.Equal(new Version(2, 5, 1), result.Version);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task TimeoutRetriesRedirectThenFallsBackToAtomFeed()
    {
        var handler = new QueueMessageHandler(
            new TaskCanceledException("timeout"),
            new TaskCanceledException("timeout"),
            Response(HttpStatusCode.OK, content: AtomFeed("v2.5.2")));
        var checker = CreateChecker(handler);

        var result = await checker.ResolveLatestReleaseAsync();

        Assert.Equal(new Version(2, 5, 2), result.Version);
        Assert.Equal(3, handler.Requests.Count);
        Assert.EndsWith("/releases.atom", handler.Requests[2].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiRateLimitIsNotUsedByFallback()
    {
        var handler = new QueueMessageHandler(
            Response(HttpStatusCode.Forbidden),
            Response(HttpStatusCode.OK, content: AtomFeed("v2.5.0")));
        var checker = CreateChecker(handler);

        var result = await checker.ResolveLatestReleaseAsync();

        Assert.Equal(new Version(2, 5, 0), result.Version);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TwoTimeoutsAndFeedTimeoutReturnTypedFailure()
    {
        var handler = new QueueMessageHandler(
            new TaskCanceledException("timeout"),
            new TaskCanceledException("timeout"),
            new TaskCanceledException("timeout"));
        var checker = CreateChecker(handler);

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(() => checker.ResolveLatestReleaseAsync());

        Assert.Equal(UpdateCheckFailureKind.Timeout, exception.Kind);
        Assert.Contains("超时", exception.Message, StringComparison.Ordinal);
    }

    private static UpdateChecker CreateChecker(QueueMessageHandler handler) =>
        new(
            new ManualReleaseVerifier(),
            () => handler,
            new Version(2, 5, 0),
            TimeSpan.FromSeconds(2),
            (_, _) => Task.CompletedTask);

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string? location = null, string? content = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (location is not null) response.Headers.Location = new Uri(location);
        if (content is not null) response.Content = new StringContent(content);
        return response;
    }

    private static string AtomFeed(string tag) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <title>{{tag}}</title>
            <link href="https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/tag/{{tag}}" />
          </entry>
        </feed>
        """;

    private sealed class QueueMessageHandler(params object[] responses) : HttpMessageHandler
    {
        private readonly Queue<object> _responses = new(responses);
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            var next = _responses.Dequeue();
            return next is Exception exception
                ? Task.FromException<HttpResponseMessage>(exception)
                : Task.FromResult((HttpResponseMessage)next);
        }
    }
}
