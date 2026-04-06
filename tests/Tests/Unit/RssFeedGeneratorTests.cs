using NUnit.Framework;
using PodScrub.Application;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class RssFeedGeneratorTests
{
    [Test]
    public void GenerateFeed_WithEpisodes_GeneratesValidRss()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", "https://example.com/image.jpg", "https://example.com");
        var episodes = new List<Episode>
        {
            new("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3", new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)),
        };
        episodes[0].MarkProcessed("/processed/ep-1.mp3", 2);

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, episodes, "http://localhost:8080", "my-feed");

        // Assert
        result.Should().Contain("<title>My Podcast</title>");
        result.Should().Contain("<title>Episode 1 [SCRUBBED]</title>");
        result.Should().Contain("http://localhost:8080/podscrub/audio/ep-1");
        result.Should().Contain("audio/mpeg");
    }

    [Test]
    public void GenerateFeed_WithUnprocessedEpisode_UsesOriginalUrl()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", null, "https://example.com");
        var episodes = new List<Episode>
        {
            new("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3", new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)),
        };

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, episodes, "http://localhost:8080", "my-feed");

        // Assert
        result.Should().Contain("https://example.com/ep1.mp3");
        result.Should().NotContain("podscrub/audio");
    }

    [Test]
    public void GenerateFeed_WithNoImage_OmitsImageElement()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", null, "https://example.com");

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, [], "http://localhost:8080", "my-feed");

        // Assert
        result.Should().NotContain("itunes:image");
    }

    [Test]
    public void GenerateFeed_WithImage_IncludesImageElement()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", "https://example.com/image.jpg", "https://example.com");

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, [], "http://localhost:8080", "my-feed");

        // Assert
        result.Should().Contain("itunes:image");
        result.Should().Contain("https://example.com/image.jpg");
    }

    [Test]
    public void GenerateFeed_WithMultipleEpisodes_SortsByPubDateDescending()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", null, "https://example.com");
        var episodes = new List<Episode>
        {
            new("ep-old", "my-feed", "Old Episode", "https://example.com/old.mp3", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new("ep-new", "my-feed", "New Episode", "https://example.com/new.mp3", new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, episodes, "http://localhost:8080", "my-feed");

        // Assert
        var newIndex = result.IndexOf("New Episode", StringComparison.Ordinal);
        var oldIndex = result.IndexOf("Old Episode", StringComparison.Ordinal);
        newIndex.Should().BeLessThan(oldIndex);
    }

    [Test]
    public void GenerateFeed_ProcessedWithoutSegments_DoesNotAppendScrubbed()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", null, "https://example.com");
        var episodes = new List<Episode>
        {
            new("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3", new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)),
        };
        episodes[0].MarkProcessed("/processed/ep-1.mp3");

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, episodes, "http://localhost:8080", "my-feed");

        // Assert
        result.Should().Contain("<title>Episode 1</title>");
        result.Should().NotContain("[SCRUBBED]");
    }

    [Test]
    public void GenerateFeed_WithEpisodeMetadata_IncludesDescriptionAndImage()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", null, "https://example.com");
        var episodes = new List<Episode>
        {
            new("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3",
                new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero))
            {
                Description = "Episode about testing",
                ImageUrl = "https://example.com/ep1-art.jpg",
                Duration = TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(15),
            },
        };

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, episodes, "http://localhost:8080", "my-feed");

        // Assert
        result.Should().Contain("<description>Episode about testing</description>");
        result.Should().Contain("itunes:summary");
        result.Should().Contain("ep1-art.jpg");
        result.Should().Contain("itunes:duration");
        result.Should().Contain("42:15");
    }

    [Test]
    public void GenerateFeed_WithoutEpisodeMetadata_OmitsOptionalElements()
    {
        // Arrange
        var metadata = new FeedMetadata("My Podcast", "A great podcast", null, "https://example.com");
        var episodes = new List<Episode>
        {
            new("ep-1", "my-feed", "Episode 1", "https://example.com/ep1.mp3",
                new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)),
        };

        // Act
        var result = RssFeedGenerator.GenerateFeed(metadata, episodes, "http://localhost:8080", "my-feed");

        // Assert
        result.Should().NotContain("itunes:summary");
        result.Should().NotContain("itunes:duration");
        // Channel description is present, but no episode-level <description> in <item>
        var itemStartIndex = result.IndexOf("<item>", StringComparison.Ordinal);
        var itemContent = result[itemStartIndex..];
        itemContent.Should().NotContain("<description>");
    }

    [Test]
    [TestCase(0, 5, 30, "5:30")]
    [TestCase(1, 23, 45, "1:23:45")]
    [TestCase(2, 0, 0, "2:00:00")]
    [TestCase(0, 0, 42, "0:42")]
    public void FormatDuration_FormatsCorrectly(int hours, int minutes, int seconds, string expected)
    {
        // Arrange
        var duration = new TimeSpan(hours, minutes, seconds);

        // Act
        var result = RssFeedGenerator.FormatDuration(duration);

        // Assert
        result.Should().Be(expected);
    }
}
