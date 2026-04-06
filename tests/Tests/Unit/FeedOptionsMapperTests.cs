using NUnit.Framework;
using PodScrub.Application;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class FeedOptionsMapperTests
{
    [Test]
    public void MapToFeeds_WithValidOptions_ReturnsFeed()
    {
        // Arrange
        var feedOptions = new List<FeedOptions>
        {
            new()
            {
                Name = "my-podcast",
                Url = "https://example.com/feed.rss",
                Jingles =
                [
                    new JingleOptions
                    {
                        Type = "InterludeStart",
                        SourceEpisode = "https://example.com/ep1.mp3",
                        TimestampStart = "00:12:34",
                        TimestampEnd = "00:12:38",
                    },
                    new JingleOptions
                    {
                        Type = "InterludeEnd",
                        SourceEpisode = "https://example.com/ep1.mp3",
                        TimestampStart = "00:15:00",
                        TimestampEnd = "00:15:04",
                    },
                ],
            },
        };

        // Act
        var feeds = FeedOptionsMapper.MapToFeeds(feedOptions);

        // Assert
        feeds.Should().HaveCount(1);
        feeds[0].Name.Should().Be("my-podcast");
        feeds[0].Url.Should().Be("https://example.com/feed.rss");
    }

    [Test]
    public void MapToFeeds_WithValidOptions_ParsesJinglesCorrectly()
    {
        // Arrange
        var feedOptions = new List<FeedOptions>
        {
            new()
            {
                Name = "my-podcast",
                Url = "https://example.com/feed.rss",
                Jingles =
                [
                    new JingleOptions
                    {
                        Type = "InterludeStart",
                        SourceEpisode = "https://example.com/ep1.mp3",
                        TimestampStart = "00:12:34",
                        TimestampEnd = "00:12:38",
                    },
                    new JingleOptions
                    {
                        Type = "InterludeEnd",
                        SourceEpisode = "https://example.com/ep1.mp3",
                        TimestampStart = "00:15:00",
                        TimestampEnd = "00:15:04",
                    },
                ],
            },
        };

        // Act
        var feeds = FeedOptionsMapper.MapToFeeds(feedOptions);

        // Assert
        feeds[0].Jingles.Should().HaveCount(2);

        feeds[0].Jingles[0].Type.Should().Be(JingleType.InterludeStart);
        feeds[0].Jingles[0].SourceEpisodeUrl.Should().Be("https://example.com/ep1.mp3");
        feeds[0].Jingles[0].TimestampStart.Should().Be(new TimeSpan(0, 12, 34));
        feeds[0].Jingles[0].TimestampEnd.Should().Be(new TimeSpan(0, 12, 38));

        feeds[0].Jingles[1].Type.Should().Be(JingleType.InterludeEnd);
        feeds[0].Jingles[1].TimestampStart.Should().Be(new TimeSpan(0, 15, 0));
        feeds[0].Jingles[1].TimestampEnd.Should().Be(new TimeSpan(0, 15, 4));
    }

    [Test]
    public void MapToFeeds_WithMultipleFeeds_ReturnsAll()
    {
        // Arrange
        var feedOptions = new List<FeedOptions>
        {
            new() { Name = "podcast-a", Url = "https://a.example.com/feed.rss" },
            new() { Name = "podcast-b", Url = "https://b.example.com/feed.rss" },
        };

        // Act
        var feeds = FeedOptionsMapper.MapToFeeds(feedOptions);

        // Assert
        feeds.Should().HaveCount(2);
        feeds[0].Name.Should().Be("podcast-a");
        feeds[1].Name.Should().Be("podcast-b");
    }

    [Test]
    public void MapToFeeds_WithEmptyList_ReturnsEmpty()
    {
        // Arrange & Act
        var feeds = FeedOptionsMapper.MapToFeeds([]);

        // Assert
        feeds.Should().BeEmpty();
    }

    [Test]
    public void MapToFeeds_WithProcessLatest_SetsValue()
    {
        // Arrange
        var feedOptions = new List<FeedOptions>
        {
            new() { Name = "podcast-limited", Url = "https://example.com/feed.rss", ProcessLatest = 10 },
        };

        // Act
        var feeds = FeedOptionsMapper.MapToFeeds(feedOptions);

        // Assert
        feeds[0].ProcessLatest.Should().Be(10);
    }

    [Test]
    public void MapToFeeds_WithoutProcessLatest_ReturnsNull()
    {
        // Arrange
        var feedOptions = new List<FeedOptions>
        {
            new() { Name = "podcast", Url = "https://example.com/feed.rss" },
        };

        // Act
        var feeds = FeedOptionsMapper.MapToFeeds(feedOptions);

        // Assert
        feeds[0].ProcessLatest.Should().BeNull();
    }

    [Test]
    public void MapToFeeds_WithJingleGroups_SetsGroupCorrectly()
    {
        // Arrange
        var feedOptions = new List<FeedOptions>
        {
            new()
            {
                Name = "multi-jingle-podcast",
                Url = "https://example.com/feed.rss",
                Jingles =
                [
                    new JingleOptions { Type = "InterludeStart", Group = "main-interlude", SourceEpisode = "https://example.com/ep1.mp3", TimestampStart = "00:12:34", TimestampEnd = "00:12:38" },
                    new JingleOptions { Type = "InterludeEnd", Group = "main-interlude", SourceEpisode = "https://example.com/ep1.mp3", TimestampStart = "00:15:00", TimestampEnd = "00:15:04" },
                    new JingleOptions { Type = "InterludeStart", Group = "sponsor", SourceEpisode = "https://example.com/ep50.mp3", TimestampStart = "00:05:10", TimestampEnd = "00:05:14" },
                    new JingleOptions { Type = "InterludeEnd", Group = "sponsor", SourceEpisode = "https://example.com/ep50.mp3", TimestampStart = "00:08:30", TimestampEnd = "00:08:34" },
                ],
            },
        };

        // Act
        var feeds = FeedOptionsMapper.MapToFeeds(feedOptions);

        // Assert
        feeds[0].Jingles.Should().HaveCount(4);
        feeds[0].Jingles.Where(jingle => string.Equals(jingle.Group, "main-interlude", StringComparison.Ordinal)).Should().HaveCount(2);
        feeds[0].Jingles.Where(jingle => string.Equals(jingle.Group, "sponsor", StringComparison.Ordinal)).Should().HaveCount(2);
    }

    [Test]
    public void MapToFeeds_WithoutJingleGroup_DefaultsToDefault()
    {
        // Arrange
        var feedOptions = new List<FeedOptions>
        {
            new()
            {
                Name = "podcast",
                Url = "https://example.com/feed.rss",
                Jingles = [new JingleOptions { Type = "InterludeStart", SourceEpisode = "https://example.com/ep1.mp3", TimestampStart = "00:12:34", TimestampEnd = "00:12:38" }],
            },
        };

        // Act
        var feeds = FeedOptionsMapper.MapToFeeds(feedOptions);

        // Assert
        feeds[0].Jingles.Should().AllSatisfy(jingle => jingle.Group.Should().Be("default"));
    }

    [Test]
    public void ParseJingleType_WithUnderscoreSeparator_ParsesCorrectly()
    {
        // Arrange & Act & Assert
        FeedOptionsMapper.ParseJingleType("interlude_start").Should().Be(JingleType.InterludeStart);
        FeedOptionsMapper.ParseJingleType("interlude_end").Should().Be(JingleType.InterludeEnd);
    }

    [Test]
    public void ParseJingleType_CaseInsensitive_ParsesCorrectly()
    {
        // Arrange & Act & Assert
        FeedOptionsMapper.ParseJingleType("InterludeStart").Should().Be(JingleType.InterludeStart);
        FeedOptionsMapper.ParseJingleType("INTERLUDEEND").Should().Be(JingleType.InterludeEnd);
    }
}
