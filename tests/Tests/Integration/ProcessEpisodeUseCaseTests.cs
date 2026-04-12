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
    public async Task ExecuteAsync_WithSegments_KeepsOriginalAndCreatesProcessedFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"podscrub-test-{Guid.NewGuid()}");
        var downloadDir = Path.Combine(tempDir, "downloads");
        Directory.CreateDirectory(downloadDir);
        var originalFile = Path.Combine(downloadDir, "ep-1.mp3");
        await File.WriteAllTextAsync(originalFile, "fake audio content");

        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var useCase = new ProcessEpisodeUseCase(
            detectInterludes,
            episodeDownloader,
            audioProcessor,
            new FileSystem(),
            Options.Create(new PodScrubOptions()),
            Substitute.For<ILogger<ProcessEpisodeUseCase>>());

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        episodeDownloader.DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), "ep-1.mp3", Arg.Any<CancellationToken>())
            .Returns(originalFile);

        var wavFile = Path.Combine(downloadDir, "ep-1.wav");
        await File.WriteAllTextAsync(wavFile, "fake wav content");
        audioProcessor.ConvertToWavAsync(originalFile, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(wavFile);
        audioProcessor.RemoveSegmentsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Interlude>>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var outputPath = callInfo.ArgAt<string>(2);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, "processed content");
                return Task.CompletedTask;
            });

        var (jingles, interludeStartJingle, interludeEndJingle) = CreateJinglesWithPaths(tempDir);
        fingerprintEngine.FindJingleMatchesAsync(wavFile, "interlude_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(120)]);
        fingerprintEngine.FindJingleMatchesAsync(wavFile, "interlude_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(180)]);

        try
        {
            // Act
            await useCase.ExecuteAsync(episode, jingles, tempDir, CancellationToken.None);

            // Assert
            File.Exists(originalFile).Should().BeTrue("original download should be kept for comparison");
            File.Exists(wavFile).Should().BeFalse("temporary WAV file should be deleted after processing");
            File.Exists(Path.Combine(tempDir, "processed", "ep-1.mp3")).Should().BeTrue("processed file should exist");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsProcessingWhenProcessedFileAlreadyExists()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"podscrub-test-{Guid.NewGuid()}");
        var processedDir = Path.Combine(tempDir, "processed");
        Directory.CreateDirectory(processedDir);
        var processedFile = Path.Combine(processedDir, "ep-1.mp3");
        await File.WriteAllTextAsync(processedFile, "already processed content");

        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = new FileSystem();
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, Options.Create(new PodScrubOptions()), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        try
        {
            // Act
            var result = await useCase.ExecuteAsync(episode, [], tempDir, CancellationToken.None);

            // Assert
            result.IsProcessed.Should().BeTrue();
            result.ProcessedAudioPath.Should().Be(processedFile);
            await episodeDownloader.DidNotReceive().DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static (IReadOnlyList<Jingle> jingles, Jingle start, Jingle end) CreateJinglesWithPaths(string baseDir)
    {
        var start = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14));
        start.SetAudioFilePath(Path.Combine(baseDir, "interlude_start.wav"));
        var end = new Jingle(JingleType.InterludeEnd, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(64));
        end.SetAudioFilePath(Path.Combine(baseDir, "interlude_end.wav"));
        return ([start, end], start, end);
    }
}
