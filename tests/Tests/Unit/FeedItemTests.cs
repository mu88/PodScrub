using NUnit.Framework;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class FeedItemTests
{
    [Test]
    public void Constructor_WithRequiredProperties_SetsDefaults()
    {
        // Arrange & Act
        var feedItem = new FeedItem("id-1", "Title", "https://example.com/ep.mp3", DateTimeOffset.UtcNow);

        // Assert
        feedItem.Id.Should().Be("id-1");
        feedItem.Title.Should().Be("Title");
        feedItem.AudioUrl.Should().Be("https://example.com/ep.mp3");
        feedItem.Description.Should().BeNull();
        feedItem.ImageUrl.Should().BeNull();
        feedItem.Duration.Should().BeNull();
    }

    [Test]
    public void Constructor_WithAllProperties_SetsMetadata()
    {
        // Arrange & Act
        var feedItem = new FeedItem(
            "id-1",
            "Title",
            "https://example.com/ep.mp3",
            DateTimeOffset.UtcNow,
            description: "A great episode",
            imageUrl: "https://example.com/art.jpg",
            duration: TimeSpan.FromMinutes(30));

        // Assert
        feedItem.Description.Should().Be("A great episode");
        feedItem.ImageUrl.Should().Be("https://example.com/art.jpg");
        feedItem.Duration.Should().Be(TimeSpan.FromMinutes(30));
    }
}
