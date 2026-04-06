using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using PodScrub.Application;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class SyncFeedUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WithNewEpisodes_ProcessesThem()
    {
        // Arrange
        var rssFeedReader = Substitute.For<IRssFeedReader>();
        var useCase = new SyncFeedUseCase(rssFeedReader, CreateProcessEpisodeMock(), CreateOptions(), Substitute.For<IFileSystem>(), Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var feed = new Feed("test-feed", "https://example.com/feed.rss", []);

        IReadOnlyList<FeedItem> feedItems =
        [
            new("ep-1", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow),
            new("ep-2", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow),
        ];
        rssFeedReader.ReadFeedItemsAsync("https://example.com/feed.rss", Arg.Any<CancellationToken>())
            .Returns(feedItems);

        // Act
        var result = await useCase.ExecuteAsync(feed, "/output", [], CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
    }

    [Test]
    public async Task ExecuteAsync_WithExistingEpisodes_SkipsThem()
    {
        // Arrange
        var rssFeedReader = Substitute.For<IRssFeedReader>();
        var useCase = new SyncFeedUseCase(rssFeedReader, CreateProcessEpisodeMock(), CreateOptions(), Substitute.For<IFileSystem>(), Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var feed = new Feed("test-feed", "https://example.com/feed.rss", []);
        var existingEpisodes = new List<Episode>
        {
            new("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow),
        };

        IReadOnlyList<FeedItem> feedItemsWithExisting =
        [
            new("ep-1", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow),
            new("ep-2", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow),
        ];
        rssFeedReader.ReadFeedItemsAsync("https://example.com/feed.rss", Arg.Any<CancellationToken>())
            .Returns(feedItemsWithExisting);

        // Act
        var result = await useCase.ExecuteAsync(feed, "/output", existingEpisodes, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
    }

    [Test]
    public async Task ExecuteAsync_WhenProcessingFails_ContinuesWithRemainingEpisodes()
    {
        // Arrange
        var rssFeedReader = Substitute.For<IRssFeedReader>();
        var processEpisode = CreateProcessEpisodeMock();
        processEpisode.ExecuteAsync(Arg.Any<Episode>(), Arg.Any<IReadOnlyList<Jingle>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Download failed"));
        var useCase = new SyncFeedUseCase(rssFeedReader, processEpisode, CreateOptions(), Substitute.For<IFileSystem>(), Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var feed = new Feed("test-feed", "https://example.com/feed.rss", []);

        IReadOnlyList<FeedItem> feedItems =
        [
            new("ep-1", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow),
        ];
        rssFeedReader.ReadFeedItemsAsync("https://example.com/feed.rss", Arg.Any<CancellationToken>())
            .Returns(feedItems);

        // Act
        var result = await useCase.ExecuteAsync(feed, "/output", [], CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ApplyProcessLatestFilter_WithNoLimit_ReturnsAllItems()
    {
        // Arrange
        var feed = new Feed("test", "https://example.com/feed.rss", []);
        IReadOnlyList<FeedItem> items =
        [
            new("ep-1", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow.AddDays(-3)),
            new("ep-2", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow.AddDays(-2)),
            new("ep-3", "Episode 3", "https://example.com/ep3.mp3", DateTimeOffset.UtcNow.AddDays(-1)),
        ];

        // Act
        var result = SyncFeedUseCase.ApplyProcessLatestFilter(items, feed);

        // Assert
        result.Should().HaveCount(3);
    }

    [Test]
    public void ApplyProcessLatestFilter_WithLimit_ReturnsOnlyLatest()
    {
        // Arrange
        var feed = new Feed("test", "https://example.com/feed.rss", [], processLatest: 2);
        IReadOnlyList<FeedItem> items =
        [
            new("ep-1", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow.AddDays(-3)),
            new("ep-2", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow.AddDays(-2)),
            new("ep-3", "Episode 3", "https://example.com/ep3.mp3", DateTimeOffset.UtcNow.AddDays(-1)),
        ];

        // Act
        var result = SyncFeedUseCase.ApplyProcessLatestFilter(items, feed);

        // Assert
        result.Should().HaveCount(2);
        result.Select(item => item.Id).Should().Contain("ep-2").And.Contain("ep-3");
        result.Select(item => item.Id).Should().NotContain("ep-1");
    }

    [Test]
    public void ApplyRetentionPolicy_UnderLimit_KeepsAll()
    {
        // Arrange
        var useCase = new SyncFeedUseCase(
            Substitute.For<IRssFeedReader>(),
            CreateProcessEpisodeMock(),
            CreateOptions(maxEpisodesPerFeed: 5),
            Substitute.For<IFileSystem>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var episodes = new List<Episode>
        {
            new("ep-1", "test", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow.AddDays(-2)),
            new("ep-2", "test", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow.AddDays(-1)),
        };

        // Act
        var result = useCase.ApplyRetentionPolicy(episodes);

        // Assert
        result.Should().HaveCount(2);
    }

    [Test]
    public void ApplyRetentionPolicy_OverLimit_EvictsOldest()
    {
        // Arrange
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        var useCase = new SyncFeedUseCase(
            Substitute.For<IRssFeedReader>(),
            CreateProcessEpisodeMock(),
            CreateOptions(maxEpisodesPerFeed: 2),
            fileSystem,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var oldEpisode = new Episode("ep-1", "test", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow.AddDays(-3));
        oldEpisode.MarkProcessed("/audio/ep1.mp3");
        var episodes = new List<Episode>
        {
            oldEpisode,
            new("ep-2", "test", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow.AddDays(-2)),
            new("ep-3", "test", "Episode 3", "https://example.com/ep3.mp3", DateTimeOffset.UtcNow.AddDays(-1)),
        };

        // Act
        var result = useCase.ApplyRetentionPolicy(episodes);

        // Assert
        result.Should().HaveCount(2);
        result.Select(episode => episode.Id).Should().Contain("ep-2").And.Contain("ep-3");
        result.Select(episode => episode.Id).Should().NotContain("ep-1");
        fileSystem.Received(1).DeleteFile("/audio/ep1.mp3");
    }

    [Test]
    public void ApplyRetentionPolicy_EvictionWithIOException_DoesNotThrow()
    {
        // Arrange
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.When(fs => fs.DeleteFile(Arg.Any<string>())).Do(_ => throw new IOException("file locked"));
        var useCase = new SyncFeedUseCase(
            Substitute.For<IRssFeedReader>(),
            CreateProcessEpisodeMock(),
            CreateOptions(maxEpisodesPerFeed: 1),
            fileSystem,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var oldEpisode = new Episode("ep-1", "test", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow.AddDays(-2));
        oldEpisode.MarkProcessed("/audio/ep1.mp3");
        var episodes = new List<Episode>
        {
            oldEpisode,
            new("ep-2", "test", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow.AddDays(-1)),
        };

        // Act
        var act = () => useCase.ApplyRetentionPolicy(episodes);

        // Assert
        act.Should().NotThrow("IOException during eviction should be caught and logged");
    }

    [Test]
    public void ApplyRetentionPolicy_EvictedWithNullPath_DoesNotCallFileSystem()
    {
        // Arrange
        var fileSystem = Substitute.For<IFileSystem>();
        var useCase = new SyncFeedUseCase(
            Substitute.For<IRssFeedReader>(),
            CreateProcessEpisodeMock(),
            CreateOptions(maxEpisodesPerFeed: 1),
            fileSystem,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var episodes = new List<Episode>
        {
            new("ep-1", "test", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow.AddDays(-2)),
            new("ep-2", "test", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow.AddDays(-1)),
        };

        // Act
        useCase.ApplyRetentionPolicy(episodes);

        // Assert
        fileSystem.DidNotReceive().FileExists(Arg.Any<string>());
        fileSystem.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public void ApplyRetentionPolicy_EvictedFileDoesNotExist_DoesNotDelete()
    {
        // Arrange
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        var useCase = new SyncFeedUseCase(
            Substitute.For<IRssFeedReader>(),
            CreateProcessEpisodeMock(),
            CreateOptions(maxEpisodesPerFeed: 1),
            fileSystem,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SyncFeedUseCase>>());

        var oldEpisode = new Episode("ep-1", "test", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow.AddDays(-2));
        oldEpisode.MarkProcessed("/audio/ep1.mp3");
        var episodes = new List<Episode>
        {
            oldEpisode,
            new("ep-2", "test", "Episode 2", "https://example.com/ep2.mp3", DateTimeOffset.UtcNow.AddDays(-1)),
        };

        // Act
        useCase.ApplyRetentionPolicy(episodes);

        // Assert
        fileSystem.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    private static IOptions<PodScrubOptions> CreateOptions(int maxEpisodesPerFeed = 50) =>
        Options.Create(new PodScrubOptions { MaxEpisodesPerFeed = maxEpisodesPerFeed });

    private static ProcessEpisodeUseCase CreateProcessEpisodeMock()
    {
        var processEpisode = Substitute.For<ProcessEpisodeUseCase>(
            Substitute.For<DetectInterludesUseCase>(Substitute.For<IFingerprintEngine>(), Substitute.For<Microsoft.Extensions.Logging.ILogger<DetectInterludesUseCase>>()),
            Substitute.For<IEpisodeDownloader>(),
            Substitute.For<IAudioProcessor>(),
            Substitute.For<IFileSystem>(),
            Options.Create(new PodScrubOptions()),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<ProcessEpisodeUseCase>>());
        processEpisode.ExecuteAsync(Arg.Any<Episode>(), Arg.Any<IReadOnlyList<Jingle>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Episode>());
        return processEpisode;
    }
}
