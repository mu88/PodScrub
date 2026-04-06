using NUnit.Framework;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class JingleTests
{
    [Test]
    public void Constructor_CreatesJingleWithCorrectProperties()
    {
        // Arrange & Act
        var jingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14));

        // Assert
        jingle.Type.Should().Be(JingleType.InterludeStart);
        jingle.SourceEpisodeUrl.Should().Be("https://example.com/ep1.mp3");
        jingle.TimestampStart.Should().Be(TimeSpan.FromSeconds(10));
        jingle.TimestampEnd.Should().Be(TimeSpan.FromSeconds(14));
        jingle.Group.Should().Be("default");
        jingle.AudioFilePath.Should().BeNull();
    }

    [Test]
    public void Constructor_WithExplicitGroup_SetsGroupProperty()
    {
        // Arrange & Act
        var jingle = new Jingle(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), "sponsor");

        // Assert
        jingle.Group.Should().Be("sponsor");
    }

    [Test]
    public void SetAudioFilePath_SetsPath()
    {
        // Arrange
        var jingle = new Jingle(JingleType.InterludeEnd, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(64));

        // Act
        jingle.SetAudioFilePath("/data/jingles/interlude_end.wav");

        // Assert
        jingle.AudioFilePath.Should().Be("/data/jingles/interlude_end.wav");
    }
}
