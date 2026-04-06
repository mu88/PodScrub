using NUnit.Framework;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class FeedTests
{
    [Test]
    public void Constructor_CreatesFeedWithCorrectProperties()
    {
        // Arrange
        var jingles = new List<Jingle>
        {
            new(JingleType.InterludeStart, "https://example.com/ep1.mp3", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14)),
        };

        // Act
        var feed = new Feed("my-podcast", "https://example.com/feed.rss", jingles);

        // Assert
        feed.Name.Should().Be("my-podcast");
        feed.Url.Should().Be("https://example.com/feed.rss");
        feed.Jingles.Should().HaveCount(1);
        feed.ProcessLatest.Should().BeNull();
    }

    [Test]
    public void Constructor_WithProcessLatest_SetsProperty()
    {
        // Arrange & Act
        var feed = new Feed("my-podcast", "https://example.com/feed.rss", [], processLatest: 10);

        // Assert
        feed.ProcessLatest.Should().Be(10);
    }
}
