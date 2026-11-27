using System.Net;
using System.Text;
using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class PostingFetchServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://1.1.1.1/pub")]
    [InlineData("http://user:pass@1.1.1.1/private")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://192.168.0.5/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.254/")]
    [InlineData("http://169.254.169.254/latest")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://100.127.255.1/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://224.0.0.1/")]
    [InlineData("http://255.255.255.255/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fd12:3456::1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    public async Task FetchAsync_rejects_non_public_targets_without_a_request(string? url)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("request should not be sent"));
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync(url);

        Assert.False(result.Success);
        Assert.Empty(handler.Requested);
    }

    [Theory]
    [InlineData("http://1.1.1.1/jobs")]
    [InlineData("https://8.8.8.8/jobs")]
    [InlineData("http://172.15.255.255/")]
    [InlineData("http://172.32.0.1/")]
    [InlineData("http://100.63.255.255/")]
    [InlineData("http://100.128.0.1/")]
    [InlineData("http://[2001:4860:4860::8888]/")]
    [InlineData("http://[::ffff:1.1.1.1]/")]
    public async Task FetchAsync_requests_literal_public_addresses(string url)
    {
        var handler = new RecordingHandler(_ => Html("ok"));
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync(url);

        Assert.True(result.Success);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task FetchAsync_does_not_follow_a_redirect_onto_a_private_address()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("1.1.1.1", request.RequestUri!.Host);
            return Redirect("http://169.254.169.254/latest/meta-data");
        });
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync("http://1.1.1.1/start");

        Assert.False(result.Success);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task FetchAsync_revalidates_each_redirect_and_stops_after_five_hops()
    {
        var handler = new RecordingHandler(request => Redirect(request.RequestUri!.GetLeftPart(UriPartial.Authority) + "/next"));
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync("https://1.1.1.1/start");

        Assert.False(result.Success);
        Assert.Equal(6, handler.Requested.Count);
        Assert.All(handler.Requested, uri => Assert.Equal("1.1.1.1", uri.Host));
    }

    [Fact]
    public async Task FetchAsync_rejects_a_redirect_without_a_location()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Found));
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync("https://8.8.8.8/start");

        Assert.False(result.Success);
        Assert.Single(handler.Requested);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FetchAsync_rejects_unsuccessful_responses(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync("https://1.1.1.1/missing");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task FetchAsync_rejects_non_html_content()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            return response;
        });
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync("https://1.1.1.1/data");

        Assert.False(result.Success);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task FetchAsync_extracts_title_and_posting_text_and_drops_chrome()
    {
        var posting = new string('A', 280) + " staff engineer role";
        var html = $"""
            <html><head>
            <meta property="og:title" content="Staff Engineer at Contoso"/>
            <title>Ignored title</title>
            </head><body>
            <script>secret-token</script>
            <nav>Home</nav>
            <article><p>{posting}</p></article>
            </body></html>
            """;
        var handler = new RecordingHandler(_ => Html(html));
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync("https://1.1.1.1/jobs/1");

        Assert.True(result.Success);
        Assert.Equal("Staff Engineer at Contoso", result.Title);
        Assert.Contains("staff engineer role", result.Text);
        Assert.DoesNotContain("secret-token", result.Text);
        Assert.DoesNotContain("Home", result.Text);
    }

    [Fact]
    public async Task FetchAsync_truncates_a_long_title_and_fails_on_empty_html()
    {
        var longTitle = new string('T', 250);
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath == "/empty"
                ? Html("<html><body>   </body></html>")
                : Html($"<html><head><title>{longTitle}</title></head><body><p>Visible posting</p></body></html>"));
        var sut = new PostingFetchService(new HttpClient(handler));

        var titled = await sut.FetchAsync("https://1.1.1.1/long");
        var empty = await sut.FetchAsync("https://1.1.1.1/empty");

        Assert.True(titled.Success);
        Assert.Equal(200, titled.Title!.Length);
        Assert.Equal(longTitle[..200], titled.Title);
        Assert.False(empty.Success);
    }

    [Fact]
    public async Task FetchAsync_returns_failure_when_the_handler_throws()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection reset"));
        var sut = new PostingFetchService(new HttpClient(handler));

        var result = await sut.FetchAsync("https://1.1.1.1/boom");

        Assert.False(result.Success);
    }

    private static HttpResponseMessage Html(string html)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<Uri> Requested { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(_respond(request));
        }
    }
}
