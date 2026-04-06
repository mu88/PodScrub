using NUnit.Framework;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class EpisodeTests
{
    [Test]
    public void Constructor_CreatesEpisodeWithCorrectProperties()
    {
        // Arrange & Act
        var episode = new Episode("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        // Assert
        episode.Id.Should().Be("ep-1");
        episode.FeedName.Should().Be("my-feed");
        episode.Title.Should().Be("Episode 1");
        episode.OriginalAudioUrl.Should().Be("https://example.com/ep1.mp3");
        episode.IsProcessed.Should().BeFalse();
        episode.ProcessedAudioPath.Should().BeNull();
    }

    [Test]
    public void MarkProcessed_SetsProcessedAudioPath()
    {
        // Arrange
        var episode = new Episode("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        // Act
        episode.MarkProcessed("/data/processed/ep-1.mp3");

        // Assert
        episode.IsProcessed.Should().BeTrue();
        episode.ProcessedAudioPath.Should().Be("/data/processed/ep-1.mp3");
        episode.SegmentsRemoved.Should().Be(0);
    }

    [Test]
    public void MarkProcessed_WithSegmentsRemoved_TracksCount()
    {
        // Arrange
        var episode = new Episode("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        // Act
        episode.MarkProcessed("/data/processed/ep-1.mp3", 3);

        // Assert
        episode.SegmentsRemoved.Should().Be(3);
    }

    [Test]
    public void Constructor_WithOptionalMetadata_SetsProperties()
    {
        // Arrange & Act
        var episode = new Episode(
            "ep-1",
            "my-feed",
            "Episode 1",
            "https://example.com/ep1.mp3",
            DateTimeOffset.UtcNow)
        {
            Description = "A description",
            ImageUrl = "https://example.com/ep1.jpg",
            Duration = TimeSpan.FromMinutes(45),
        };

        // Assert
        episode.Description.Should().Be("A description");
        episode.ImageUrl.Should().Be("https://example.com/ep1.jpg");
        episode.Duration.Should().Be(TimeSpan.FromMinutes(45));
    }

    [Test]
    public void Constructor_WithoutOptionalMetadata_DefaultsToNull()
    {
        // Arrange & Act
        var episode = new Episode("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3", DateTimeOffset.UtcNow);

        // Assert
        episode.Description.Should().BeNull();
        episode.ImageUrl.Should().BeNull();
        episode.Duration.Should().BeNull();
    }
}
