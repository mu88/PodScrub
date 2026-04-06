using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using NSubstitute;
using NUnit.Framework;
using PodScrub.Domain;

namespace Tests.Integration.Api;

[TestFixture]
[Category("Integration")]
public class ProgramTests
{
    [Test]
    public async Task GetHealthz_ReturnsHealthy()
    {
        // Arrange
        await using var factory = new PodScrubWebApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/podscrub/healthz");

        // Assert
        response.Should().Be200Ok();
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Test]
    public async Task GetFeed_UnknownFeed_ReturnsNotFound()
    {
        // Arrange
        await using var factory = new PodScrubWebApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/podscrub/feed/nonexistent/rss.xml");

        // Assert
        response.StatusCode.Should().Be(global::System.Net.HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetFeed_ExistingFeed_ReturnsRssXml()
    {
        // Arrange
        var feed = new Feed("my-podcast", "https://example.com/feed.rss", []);
        var rssFeedReader = Substitute.For<IRssFeedReader>();
        rssFeedReader.ReadFeedMetadataAsync("https://example.com/feed.rss", Arg.Any<CancellationToken>())
            .Returns(new FeedMetadata("My Podcast", "A great show", null, "https://example.com"));

        var episodeStore = new ConcurrentDictionary<string, List<Episode>>();
        var episode = new Episode("ep-1", "my-podcast", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        episode.MarkProcessed("/processed/ep1.mp3");
        episodeStore["my-podcast"] = [episode];

        await using var factory = new PodScrubWebApplicationFactory([feed], rssFeedReader, episodeStore);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/podscrub/feed/my-podcast/rss.xml");

        // Assert
        response.Should().Be200Ok();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/rss+xml");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("My Podcast");
        content.Should().Contain("Episode 1");
        content.Should().Contain("ep-1");
    }

    [Test]
    public async Task GetFeed_IsCaseInsensitive()
    {
        // Arrange
        var feed = new Feed("my-podcast", "https://example.com/feed.rss", []);
        var rssFeedReader = Substitute.For<IRssFeedReader>();
        rssFeedReader.ReadFeedMetadataAsync("https://example.com/feed.rss", Arg.Any<CancellationToken>())
            .Returns(new FeedMetadata("My Podcast", "A great show", null, "https://example.com"));

        await using var factory = new PodScrubWebApplicationFactory([feed], rssFeedReader);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/podscrub/feed/My-Podcast/rss.xml");

        // Assert
        response.Should().Be200Ok();
    }

    [Test]
    public async Task GetAudio_NonExistentEpisode_ReturnsNotFound()
    {
        // Arrange
        await using var factory = new PodScrubWebApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/podscrub/audio/nonexistent");

        // Assert
        response.StatusCode.Should().Be(global::System.Net.HttpStatusCode.NotFound);
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Test method; single use is acceptable")]
    public async Task GetAudio_ExistingEpisode_ReturnsAudioFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, [0xFF, 0xFB, 0x90, 0x00]);

        var episodeStore = new ConcurrentDictionary<string, List<Episode>>();
        var episode = new Episode("ep-1", "my-podcast", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        episode.MarkProcessed(tempFile);
        episodeStore["my-podcast"] = [episode];

        await using var factory = new PodScrubWebApplicationFactory(episodeStore: episodeStore);
        using var client = factory.CreateClient();

        try
        {
            // Act
            var response = await client.GetAsync("/podscrub/audio/ep-1");

            // Assert
            response.Should().Be200Ok();
            response.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            bytes.Should().HaveCountGreaterThan(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetAudio_EpisodeWithMissingFile_ReturnsNotFound()
    {
        // Arrange
        var episodeStore = new ConcurrentDictionary<string, List<Episode>>();
        var episode = new Episode("ep-1", "my-podcast", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        episode.MarkProcessed("/nonexistent/path/audio.mp3");
        episodeStore["my-podcast"] = [episode];

        await using var factory = new PodScrubWebApplicationFactory(episodeStore: episodeStore);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/podscrub/audio/ep-1");

        // Assert
        response.StatusCode.Should().Be(global::System.Net.HttpStatusCode.NotFound);
    }
}
