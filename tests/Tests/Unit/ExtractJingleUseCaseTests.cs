using NSubstitute;
using NUnit.Framework;
using PodScrub.Application;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class ExtractJingleUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_DownloadsEpisodeAndExtractsClip()
    {
        // Arrange
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<ExtractJingleUseCase>>();
        var useCase = new ExtractJingleUseCase(episodeDownloader, audioProcessor, fileSystem, logger);

        var jingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(34)), TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(38)));

        episodeDownloader.DownloadEpisodeAsync("https://example.com/ep1.mp3", "/jingles", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("/jingles/ep1.mp3");

        // Act
        var result = await useCase.ExecuteAsync(jingle, "/jingles", CancellationToken.None);

        // Assert
        result.Should().StartWith(Path.Combine("/jingles", "jingle_InterludeStart_"));
        result.Should().EndWith(".wav");
        result.Should().NotContain("00000000", "filename should be a deterministic hash, not a GUID segment");
        jingle.AudioFilePath.Should().Be(result);

        await episodeDownloader.Received(1).DownloadEpisodeAsync("https://example.com/ep1.mp3", "/jingles", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await audioProcessor.Received(1).ExtractClipAsync(
            "/jingles/ep1.mp3",
            jingle.TimestampStart,
            jingle.TimestampEnd,
            Arg.Is<string>(path => path.Contains("jingle_InterludeStart_", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());

        // Source episode is NOT deleted by ExtractJingleUseCase — cleanup happens at the caller level
        fileSystem.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public async Task ExecuteAsync_WhenJingleAlreadyExists_SkipsDownloadAndExtraction()
    {
        // Arrange
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<ExtractJingleUseCase>>();
        var useCase = new ExtractJingleUseCase(episodeDownloader, audioProcessor, fileSystem, logger);

        var jingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(4)));

        // Act
        var result = await useCase.ExecuteAsync(jingle, "/jingles", CancellationToken.None);

        // Assert
        result.Should().StartWith(Path.Combine("/jingles", "jingle_InterludeStart_"));
        jingle.AudioFilePath.Should().Be(result);
        await episodeDownloader.DidNotReceive().DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await audioProcessor.DidNotReceive().ExtractClipAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void CreateUrlHash_ReturnsSixteenCharHexString()
    {
        // Arrange
        const string url = "https://example.com/ep1.mp3";

        // Act
        var hash = ExtractJingleUseCase.CreateUrlHash(url);

        // Assert
        hash.Should().HaveLength(16);
        hash.Should().MatchRegex("^[0-9a-f]+$", "hash should be lowercase hex");
    }

    [Test]
    public void CreateUrlHash_SameUrlProducesSameHash()
    {
        // Arrange
        const string url = "https://example.com/ep1.mp3";

        // Act
        var hash1 = ExtractJingleUseCase.CreateUrlHash(url);
        var hash2 = ExtractJingleUseCase.CreateUrlHash(url);

        // Assert
        hash1.Should().Be(hash2);
    }
}
