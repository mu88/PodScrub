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
        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<ExtractJingleUseCase>>();
        var useCase = new ExtractJingleUseCase(episodeDownloader, audioProcessor, fileSystem, logger);

        var jingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(34)), TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(38)));

        episodeDownloader.DownloadEpisodeAsync("https://example.com/ep1.mp3", "/jingles", Arg.Any<CancellationToken>())
            .Returns("/jingles/ep1.mp3");

        // Act
        var result = await useCase.ExecuteAsync(jingle, "/jingles", CancellationToken.None);

        // Assert
        result.Should().StartWith(Path.Combine("/jingles", "jingle_InterludeStart_"));
        result.Should().EndWith(".wav");
        jingle.AudioFilePath.Should().Be(result);

        await episodeDownloader.Received(1).DownloadEpisodeAsync("https://example.com/ep1.mp3", "/jingles", Arg.Any<CancellationToken>());
        await audioProcessor.Received(1).ExtractClipAsync(
            "/jingles/ep1.mp3",
            jingle.TimestampStart,
            jingle.TimestampEnd,
            Arg.Is<string>(path => path.Contains("jingle_InterludeStart_", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        fileSystem.Received(1).DeleteFile("/jingles/ep1.mp3");
    }

    [Test]
    public async Task ExecuteAsync_WhenSourceCleanupThrowsIOException_DoesNotThrow()
    {
        // Arrange
        var episodeDownloader = Substitute.For<IEpisodeDownloader>();
        var audioProcessor = Substitute.For<IAudioProcessor>();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.When(fs => fs.DeleteFile(Arg.Any<string>())).Do(_ => throw new IOException("file locked"));
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<ExtractJingleUseCase>>();
        var useCase = new ExtractJingleUseCase(episodeDownloader, audioProcessor, fileSystem, logger);

        var jingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(34)), TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(38)));

        episodeDownloader.DownloadEpisodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("/jingles/ep1.mp3");

        // Act
        var act = () => useCase.ExecuteAsync(jingle, "/jingles", CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync("IOException during source cleanup should be caught and logged");
    }
}
