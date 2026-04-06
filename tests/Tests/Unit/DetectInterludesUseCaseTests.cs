using NSubstitute;
using NUnit.Framework;
using PodScrub.Application;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class DetectInterludesUseCaseTests
{
    [Test]
    public void PairInterludes_WithMatchingStartsAndEnds_CreatesInterludes()
    {
        // Arrange
        var starts = new List<TimeSpan> { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60) };
        var ends = new List<TimeSpan> { TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(80) };

        // Act
        var result = DetectInterludesUseCase.PairInterludes(starts, ends);

        // Assert
        result.Should().HaveCount(2);
        result[0].Start.Should().Be(TimeSpan.FromSeconds(10));
        result[0].End.Should().Be(TimeSpan.FromSeconds(25));
        result[1].Start.Should().Be(TimeSpan.FromSeconds(60));
        result[1].End.Should().Be(TimeSpan.FromSeconds(80));
    }

    [Test]
    public void PairInterludes_WithMoreStartsThanEnds_PairsOnlyAvailable()
    {
        // Arrange
        var starts = new List<TimeSpan> { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60) };
        var ends = new List<TimeSpan> { TimeSpan.FromSeconds(25) };

        // Act
        var result = DetectInterludesUseCase.PairInterludes(starts, ends);

        // Assert
        result.Should().HaveCount(1);
        result[0].Start.Should().Be(TimeSpan.FromSeconds(10));
        result[0].End.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Test]
    public void PairInterludes_WithEmptyLists_ReturnsEmpty()
    {
        // Arrange
        var starts = new List<TimeSpan>();
        var ends = new List<TimeSpan>();

        // Act
        var result = DetectInterludesUseCase.PairInterludes(starts, ends);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void PairInterludes_WithEndBeforeStart_SkipsUnmatchable()
    {
        // Arrange
        var starts = new List<TimeSpan> { TimeSpan.FromSeconds(30) };
        var ends = new List<TimeSpan> { TimeSpan.FromSeconds(10) };

        // Act
        var result = DetectInterludesUseCase.PairInterludes(starts, ends);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void PairInterludes_WithUnsortedInput_SortsAndPairsCorrectly()
    {
        // Arrange
        var starts = new List<TimeSpan> { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10) };
        var ends = new List<TimeSpan> { TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(25) };

        // Act
        var result = DetectInterludesUseCase.PairInterludes(starts, ends);

        // Assert
        result.Should().HaveCount(2);
        result[0].Start.Should().Be(TimeSpan.FromSeconds(10));
        result[0].End.Should().Be(TimeSpan.FromSeconds(25));
        result[1].Start.Should().Be(TimeSpan.FromSeconds(60));
        result[1].End.Should().Be(TimeSpan.FromSeconds(80));
    }

    [Test]
    public async Task ExecuteAsync_WithJinglesWithoutAudioPath_ReturnsEmpty()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<DetectInterludesUseCase>>();
        var useCase = new DetectInterludesUseCase(fingerprintEngine, logger);

        var jingles = new List<Jingle>
        {
            new(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14)),
        };

        // Act
        var result = await useCase.ExecuteAsync("/audio/episode.mp3", jingles, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        await fingerprintEngine.DidNotReceive().FindJingleMatchesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WithMatchingJingles_ReturnsInterludes()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<DetectInterludesUseCase>>();
        var useCase = new DetectInterludesUseCase(fingerprintEngine, logger);

        var interludeStartJingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14));
        interludeStartJingle.SetAudioFilePath("/jingles/interlude_start.wav");

        var interludeEndJingle = new Jingle(JingleType.InterludeEnd, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(64));
        interludeEndJingle.SetAudioFilePath("/jingles/interlude_end.wav");

        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "interlude_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(120)]);
        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "interlude_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(180)]);

        var jingles = new List<Jingle> { interludeStartJingle, interludeEndJingle };

        // Act
        var result = await useCase.ExecuteAsync("/audio/episode.mp3", jingles, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Start.Should().Be(TimeSpan.FromSeconds(120));
        result[0].End.Should().Be(TimeSpan.FromSeconds(180));
    }

    [Test]
    public async Task ExecuteAsync_WithMultipleGroups_PairsWithinGroups()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<DetectInterludesUseCase>>();
        var useCase = new DetectInterludesUseCase(fingerprintEngine, logger);

        var mainStart = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), "main-interlude");
        mainStart.SetAudioFilePath("/jingles/main_start.wav");
        var mainEnd = new Jingle(JingleType.InterludeEnd, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(64), "main-interlude");
        mainEnd.SetAudioFilePath("/jingles/main_end.wav");

        var sponsorStart = new Jingle(JingleType.InterludeStart, "https://example.com/ep50.mp3", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), "sponsor");
        sponsorStart.SetAudioFilePath("/jingles/sponsor_start.wav");
        var sponsorEnd = new Jingle(JingleType.InterludeEnd, "https://example.com/ep50.mp3", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(34), "sponsor");
        sponsorEnd.SetAudioFilePath("/jingles/sponsor_end.wav");

        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "main_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(120)]);
        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "main_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(180)]);
        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "sponsor_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(300)]);
        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "sponsor_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(360)]);

        var jingles = new List<Jingle> { mainStart, mainEnd, sponsorStart, sponsorEnd };

        // Act
        var result = await useCase.ExecuteAsync("/audio/episode.mp3", jingles, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(segment => segment.Start == TimeSpan.FromSeconds(120) && segment.End == TimeSpan.FromSeconds(180));
        result.Should().Contain(segment => segment.Start == TimeSpan.FromSeconds(300) && segment.End == TimeSpan.FromSeconds(360));
    }

    [Test]
    public async Task ExecuteAsync_WithGroupHavingOnlyStart_DoesNotCrossPairWithOtherGroup()
    {
        // Arrange
        var fingerprintEngine = Substitute.For<IFingerprintEngine>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<DetectInterludesUseCase>>();
        var useCase = new DetectInterludesUseCase(fingerprintEngine, logger);

        var groupAStart = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), "group-a");
        groupAStart.SetAudioFilePath("/jingles/a_start.wav");

        var groupBEnd = new Jingle(JingleType.InterludeEnd, "https://example.com/ep50.mp3", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(34), "group-b");
        groupBEnd.SetAudioFilePath("/jingles/b_end.wav");

        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "a_start", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(120)]);
        fingerprintEngine.FindJingleMatchesAsync("/audio/episode.mp3", "b_end", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TimeSpan>)[TimeSpan.FromSeconds(180)]);

        var jingles = new List<Jingle> { groupAStart, groupBEnd };

        // Act
        var result = await useCase.ExecuteAsync("/audio/episode.mp3", jingles, CancellationToken.None);

        // Assert
        result.Should().BeEmpty("start from group-a must not pair with end from group-b");
    }
}
