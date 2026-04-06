using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using PodScrub.Infrastructure;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class AllowListHttpMessageHandlerTests
{
    [Test]
    public void Constructor_ExtractsHostsFromUrls()
    {
        // Arrange
        var urls = new[] { "https://feeds.example.com/feed.rss", "https://cdn.podcast.org/audio/" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();

        // Act
        var handler = new AllowListHttpMessageHandler(urls, logger, new HttpClientHandler());

        // Assert
        handler.AllowedHosts.Should().Contain("feeds.example.com");
        handler.AllowedHosts.Should().Contain("cdn.podcast.org");
    }

    [Test]
    public void Constructor_TwoParameters_UsesDefaultInnerHandler()
    {
        // Arrange
        var urls = new[] { "https://feeds.example.com/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();

        // Act
        using var handler = new AllowListHttpMessageHandler(urls, logger);

        // Assert
        handler.AllowedHosts.Should().Contain("feeds.example.com");
    }

    [Test]
    public void Constructor_IgnoresInvalidUrls()
    {
        // Arrange
        var urls = new[] { "not-a-url", "https://valid.example.com/feed" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();

        // Act
        var handler = new AllowListHttpMessageHandler(urls, logger, new HttpClientHandler());

        // Assert
        handler.AllowedHosts.Should().HaveCount(1);
        handler.AllowedHosts.Should().Contain("valid.example.com");
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Test method; single use is acceptable")]
    public async Task SendAsync_AllowedHost_PassesThrough()
    {
        // Arrange
        var urls = new[] { "https://feeds.example.com/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var innerHandler = new FakeHttpMessageHandler(new HttpResponseMessage(global::System.Net.HttpStatusCode.OK));
        var handler = new AllowListHttpMessageHandler(urls, logger, innerHandler);
        using var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("https://feeds.example.com/some/path");

        // Assert
        response.StatusCode.Should().Be(global::System.Net.HttpStatusCode.OK);
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Test method; single use is acceptable")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP013:Await in using", Justification = "FluentAssertions ThrowAsync pattern")]
    public async Task SendAsync_BlockedHost_ThrowsInvalidOperationException()
    {
        // Arrange
        var urls = new[] { "https://feeds.example.com/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var innerHandler = new FakeHttpMessageHandler(new HttpResponseMessage(global::System.Net.HttpStatusCode.OK));
        var handler = new AllowListHttpMessageHandler(urls, logger, innerHandler);
        using var client = new HttpClient(handler);

        // Act
        var act = () => client.GetAsync("https://evil.attacker.com/steal-data");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*evil.attacker.com*not allowed*");
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Test method; single use is acceptable")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP013:Await in using", Justification = "FluentAssertions ThrowAsync pattern")]
    public async Task SendAsync_NullRequestUri_ThrowsInvalidOperationException()
    {
        // Arrange
        var urls = new[] { "https://feeds.example.com/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var innerHandler = new FakeHttpMessageHandler(new HttpResponseMessage(global::System.Net.HttpStatusCode.OK));
        var handler = new AllowListHttpMessageHandler(urls, logger, innerHandler);
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        // Act
        var act = () => invoker.SendAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Request URI must not be null*");
    }

    [Test]
    public void Constructor_IsCaseInsensitiveForHosts()
    {
        // Arrange
        var urls = new[] { "https://FEEDS.Example.COM/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();

        // Act
        var handler = new AllowListHttpMessageHandler(urls, logger, new HttpClientHandler());

        // Assert
        handler.AllowedHosts.Should().Contain("FEEDS.Example.COM");
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Test method; single use is acceptable")]
    public async Task SendAsync_SubdomainOfAllowedHost_PassesThrough()
    {
        // Arrange
        var urls = new[] { "https://omny.fm/shows/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var innerHandler = new FakeHttpMessageHandler(new HttpResponseMessage(global::System.Net.HttpStatusCode.OK));
        var handler = new AllowListHttpMessageHandler(urls, logger, innerHandler);
        using var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("https://traffic.omny.fm/d/clips/audio.mp3");

        // Assert
        response.StatusCode.Should().Be(global::System.Net.HttpStatusCode.OK);
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Test method; single use is acceptable")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP013:Await in using", Justification = "FluentAssertions ThrowAsync pattern")]
    public async Task SendAsync_SimilarButNotSubdomain_ThrowsInvalidOperationException()
    {
        // Arrange
        var urls = new[] { "https://omny.fm/shows/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var innerHandler = new FakeHttpMessageHandler(new HttpResponseMessage(global::System.Net.HttpStatusCode.OK));
        var handler = new AllowListHttpMessageHandler(urls, logger, innerHandler);
        using var client = new HttpClient(handler);

        // Act
        var act = () => client.GetAsync("https://notomny.fm/evil");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*notomny.fm*not allowed*");
    }

    [Test]
    public void IsHostAllowed_ExactMatch_ReturnsTrue()
    {
        // Arrange
        var urls = new[] { "https://example.com/feed" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var handler = new AllowListHttpMessageHandler(urls, logger, new HttpClientHandler());

        // Act & Assert
        handler.IsHostAllowed("example.com").Should().BeTrue();
    }

    [Test]
    public void IsHostAllowed_SubdomainMatch_ReturnsTrue()
    {
        // Arrange
        var urls = new[] { "https://example.com/feed" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var handler = new AllowListHttpMessageHandler(urls, logger, new HttpClientHandler());

        // Act & Assert
        handler.IsHostAllowed("cdn.example.com").Should().BeTrue();
    }

    [Test]
    public void IsHostAllowed_UnrelatedHost_ReturnsFalse()
    {
        // Arrange
        var urls = new[] { "https://example.com/feed" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var handler = new AllowListHttpMessageHandler(urls, logger, new HttpClientHandler());

        // Act & Assert
        handler.IsHostAllowed("evil.com").Should().BeFalse();
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Test method; single use is acceptable")]
    public async Task SendAsync_CaseInsensitiveHostMatch_PassesThrough()
    {
        // Arrange
        var urls = new[] { "https://FEEDS.EXAMPLE.COM/feed.rss" };
        var logger = Substitute.For<ILogger<AllowListHttpMessageHandler>>();
        var innerHandler = new FakeHttpMessageHandler(new HttpResponseMessage(global::System.Net.HttpStatusCode.OK));
        var handler = new AllowListHttpMessageHandler(urls, logger, innerHandler);
        using var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("https://feeds.example.com/some/path");

        // Assert
        response.StatusCode.Should().Be(global::System.Net.HttpStatusCode.OK);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHttpMessageHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }
}
