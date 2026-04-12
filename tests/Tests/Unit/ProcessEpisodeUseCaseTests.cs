using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using PodScrub.Application;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class ProcessEpisodeUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WithNoInterludes_CopiesOriginalToProcessed()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        var downloadedPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.mp3");
        var wavPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.wav");

        episodeDownloader.DownloadEpisodeAsync("https://example.com/ep1.mp3", Arg.Any<string>(), "ep-1.mp3", Arg.Any<CancellationToken>())
            .Returns(downloadedPath);
        audioProcessor.ConvertToWavAsync(downloadedPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(wavPath);

        // Act
        var result = await useCase.ExecuteAsync(episode, [], Path.GetTempPath(), CancellationToken.None);

        // Assert
        result.IsProcessed.Should().BeTrue();
        result.ProcessedAudioPath.Should().Contain(Path.Combine("processed", "ep-1.mp3"));
        fileSystem.Received(1).CopyFile(downloadedPath, Arg.Is<string>(path => path.Contains("ep-1.mp3", StringComparison.Ordinal)));
        await audioProcessor.DidNotReceive().RemoveSegmentsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Interlude>>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WithNoInterludes_CreatesRequiredDirectories()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        episodeDownloader.DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Path.Combine(Path.GetTempPath(), "downloads", "ep-1.mp3"));
        audioProcessor.ConvertToWavAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Path.Combine(Path.GetTempPath(), "downloads", "ep-1.wav"));

        // Act
        await useCase.ExecuteAsync(episode, [], Path.GetTempPath(), CancellationToken.None);

        // Assert
        fileSystem.Received(1).CreateDirectory(Arg.Is<string>(path => path.Contains("processed", StringComparison.Ordinal)));
        fileSystem.Received(1).CreateDirectory(Arg.Is<string>(path => path.Contains("downloads", StringComparison.Ordinal)));
    }

    [Test]
    public async Task ExecuteAsync_WithInterludes_RemovesThem()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Is<string>(path => path.Contains("processed", StringComparison.Ordinal))).Returns(false);
        fileSystem.FileExists(Arg.Is<string>(path => !path.Contains("processed", StringComparison.Ordinal))).Returns(true);
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions("/data/transition.mp3"), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        var downloadedPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.mp3");
        var wavPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.wav");

        episodeDownloader.DownloadEpisodeAsync("https://example.com/ep1.mp3", Arg.Any<string>(), "ep-1.mp3", Arg.Any<CancellationToken>())
            .Returns(downloadedPath);
        audioProcessor.ConvertToWavAsync(downloadedPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(wavPath);

        var interludeStartJingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14));
        interludeStartJingle.SetAudioFilePath("/jingles/interlude_start.wav");
        var interludeEndJingle = new Jingle(JingleType.InterludeEnd, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(64));
        interludeEndJingle.SetAudioFilePath("/jingles/interlude_end.wav");
        IReadOnlyList<Jingle> jingles = [interludeStartJingle, interludeEndJingle];

        fingerprintEngine.FindJingleMatchesAsync(wavPath, "interlude_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(120)]);
        fingerprintEngine.FindJingleMatchesAsync(wavPath, "interlude_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(180)]);

        // Act
        var result = await useCase.ExecuteAsync(episode, jingles, Path.GetTempPath(), CancellationToken.None);

        // Assert
        result.IsProcessed.Should().BeTrue();
        await audioProcessor.Received(1).RemoveSegmentsAsync(downloadedPath, Arg.Any<IReadOnlyList<Interlude>>(), Arg.Any<string>(), "/data/transition.mp3", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WithInterludes_WritesSidecarJson()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Is<string>(path => path.Contains("processed", StringComparison.Ordinal))).Returns(false);
        fileSystem.FileExists(Arg.Is<string>(path => !path.Contains("processed", StringComparison.Ordinal))).Returns(true);
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        var downloadedPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.mp3");
        var wavPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.wav");

        episodeDownloader.DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(downloadedPath);
        audioProcessor.ConvertToWavAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(wavPath);

        var interludeStartJingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14));
        interludeStartJingle.SetAudioFilePath("/jingles/interlude_start.wav");
        var interludeEndJingle = new Jingle(JingleType.InterludeEnd, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(64));
        interludeEndJingle.SetAudioFilePath("/jingles/interlude_end.wav");
        IReadOnlyList<Jingle> jingles = [interludeStartJingle, interludeEndJingle];

        fingerprintEngine.FindJingleMatchesAsync(wavPath, "interlude_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(120)]);
        fingerprintEngine.FindJingleMatchesAsync(wavPath, "interlude_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(180)]);

        // Act
        await useCase.ExecuteAsync(episode, jingles, Path.GetTempPath(), CancellationToken.None);

        // Assert
        fileSystem.Received(1).WriteAllText(
            Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal) && path.Contains("ep-1", StringComparison.Ordinal)),
            Arg.Is<string>(json => json.Contains("SegmentsRemoved", StringComparison.Ordinal)));
    }

    [Test]
    public async Task ExecuteAsync_WhenAlreadyProcessed_SkipsProcessing()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Is<string>(path => path.Contains("processed", StringComparison.Ordinal))).Returns(true);
        fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal))).Returns(false);
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        // Act
        var result = await useCase.ExecuteAsync(episode, [], Path.GetTempPath(), CancellationToken.None);

        // Assert
        result.IsProcessed.Should().BeTrue();
        result.ProcessedAudioPath.Should().Contain(Path.Combine("processed", "ep-1.mp3"));
        await episodeDownloader.DidNotReceive().DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await audioProcessor.DidNotReceive().ConvertToWavAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenAlreadyProcessedWithSidecar_RestoresSegmentsRemoved()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".mp3", StringComparison.Ordinal) && path.Contains("processed", StringComparison.Ordinal))).Returns(true);
        fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal))).Returns(true);
        fileSystem.ReadAllText(Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal)))
            .Returns("""{"SegmentsRemoved":3}""");
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        // Act
        var result = await useCase.ExecuteAsync(episode, [], Path.GetTempPath(), CancellationToken.None);

        // Assert
        result.SegmentsRemoved.Should().Be(3, "segments removed count should be restored from sidecar JSON");
    }

    [Test]
    public async Task ExecuteAsync_WhenAlreadyProcessedWithMalformedSidecar_DefaultsToZeroSegments()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".mp3", StringComparison.Ordinal) && path.Contains("processed", StringComparison.Ordinal))).Returns(true);
        fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal))).Returns(true);
        fileSystem.ReadAllText(Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal)))
            .Returns("not valid json");
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        // Act
        var result = await useCase.ExecuteAsync(episode, [], Path.GetTempPath(), CancellationToken.None);

        // Assert
        result.SegmentsRemoved.Should().Be(0, "malformed sidecar should default to zero segments removed");
    }

    [Test]
    public async Task ExecuteAsync_WhenCleanupThrowsIOException_DoesNotThrow()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Is<string>(path => path.Contains("processed", StringComparison.Ordinal))).Returns(false);
        fileSystem.FileExists(Arg.Is<string>(path => !path.Contains("processed", StringComparison.Ordinal))).Returns(true);
        fileSystem.When(fs => fs.DeleteFile(Arg.Any<string>())).Do(_ => throw new IOException("file locked"));
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        var downloadedPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.mp3");
        var wavPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.wav");

        episodeDownloader.DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(downloadedPath);
        audioProcessor.ConvertToWavAsync(downloadedPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(wavPath);

        // Act
        var act = () => useCase.ExecuteAsync(episode, [], Path.GetTempPath(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync("IOException during WAV cleanup should be caught and logged");
    }

    [Test]
    public async Task ExecuteAsync_SkipsWavCleanupWhenFileDoesNotExist()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var detectInterludes = new DetectInterludesUseCase(fingerprintEngine, Substitute.For<ILogger<DetectInterludesUseCase>>());
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        var logger = Substitute.For<ILogger<ProcessEpisodeUseCase>>();
        var useCase = new ProcessEpisodeUseCase(detectInterludes, episodeDownloader, audioProcessor, fileSystem, CreateOptions(), logger);

        var episode = new Episode("ep-1", "test-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);
        var downloadedPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.mp3");
        var wavPath = Path.Combine(Path.GetTempPath(), "downloads", "ep-1.wav");

        episodeDownloader.DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(downloadedPath);
        audioProcessor.ConvertToWavAsync(downloadedPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(wavPath);

        // Act
        await useCase.ExecuteAsync(episode, [], Path.GetTempPath(), CancellationToken.None);

        // Assert
        fileSystem.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    private static IOptions<PodScrubOptions> CreateOptions(string? transitionTonePath = null) =>
        Options.Create(new PodScrubOptions { TransitionTonePath = transitionTonePath });
}
