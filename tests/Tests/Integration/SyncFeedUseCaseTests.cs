using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using PodScrub.Application;
using PodScrub.Domain;
using PodScrub.Infrastructure;

namespace Tests.Integration;

[TestFixture]
[Category("Integration")]
public class SyncFeedUseCaseTests
{
    [Test]
    public void ApplyRetentionPolicy_OverLimit_DeletesEvictedFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"podscrub-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var oldFile = Path.Combine(tempDir, "old-episode.mp3");
        File.WriteAllText(oldFile, "old audio");

        var useCase = new SyncFeedUseCase(
            Substitute.For<IRssFeedReader>(),
            CreateProcessEpisodeMock(),
            Options.Create(new PodScrubOptions { MaxEpisodesPerFeed = 1 }),
            new FileSystem(),
            Substitute.For<ILogger<SyncFeedUseCase>>());

        var oldEpisode = new Episode("ep-old", "test", "Old Episode", "https://example.com/old.mp3", DateTimeOffset.UtcNow.AddDays(-10));
        oldEpisode.MarkProcessed(oldFile);
        var newEpisode = new Episode("ep-new", "test", "New Episode", "https://example.com/new.mp3", DateTimeOffset.UtcNow);
        newEpisode.MarkProcessed(Path.Combine(tempDir, "new-episode.mp3"));

        var episodes = new List<Episode> { oldEpisode, newEpisode };

        try
        {
            // Act
            var result = useCase.ApplyRetentionPolicy(episodes);

            // Assert
            result.Should().HaveCount(1);
            result.Should().Contain(newEpisode);
            File.Exists(oldFile).Should().BeFalse("evicted episode's audio file should be deleted");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static ProcessEpisodeUseCase CreateProcessEpisodeMock()
    {
        var processEpisode = Substitute.For<ProcessEpisodeUseCase>(
            Substitute.For<DetectInterludesUseCase>(Substitute.For<IFingerprintEngine>(), Substitute.For<ILogger<DetectInterludesUseCase>>()),
            Substitute.For<IEpisodeDownloader>(),
            Substitute.For<IAudioProcessor>(),
            Substitute.For<IFileSystem>(),
            Options.Create(new PodScrubOptions()),
            Substitute.For<ILogger<ProcessEpisodeUseCase>>());
        processEpisode.ExecuteAsync(Arg.Any<Episode>(), Arg.Any<IReadOnlyList<Jingle>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Episode>());
        return processEpisode;
    }
}
