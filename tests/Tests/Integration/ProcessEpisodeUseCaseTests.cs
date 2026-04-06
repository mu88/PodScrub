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
public class ProcessEpisodeUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WithSegments_DeletesOriginalFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"podscrub-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var downloadDir = Path.Combine(tempDir, "downloads");
        Directory.CreateDirectory(downloadDir);
        var originalFile = Path.Combine(downloadDir, "episode.mp3");
        await File.WriteAllTextAsync(originalFile, "fake audio content");

        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = new FileSystem();
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, Options.Create(new PodScrubOptions()), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        episodeDownloader.DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(originalFile);

        var wavFile = Path.Combine(downloadDir, "episode.wav");
        audioProcessor.ConvertToWavAsync(originalFile, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(wavFile);

        var interludeStartJingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14));
        interludeStartJingle.SetAudioFilePath(Path.Combine(tempDir, "interlude_start.wav"));
        var interludeEndJingle = new Jingle(JingleType.InterludeEnd, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(64));
        interludeEndJingle.SetAudioFilePath(Path.Combine(tempDir, "interlude_end.wav"));
        IReadOnlyList<Jingle> jingles = [interludeStartJingle, interludeEndJingle];

        fingerprintEngine.FindJingleMatchesAsync(wavFile, "interlude_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(120)]);
        fingerprintEngine.FindJingleMatchesAsync(wavFile, "interlude_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(180)]);

        try
        {
            // Act
            await useCase.ExecuteAsync(episode, jingles, tempDir, CancellationToken.None);

            // Assert
            File.Exists(originalFile).Should().BeFalse("original file should be deleted after processing");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
