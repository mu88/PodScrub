namespace PodScrub.Domain;

public class FeedItem
{
    public FeedItem(string id, string title, string audioUrl, DateTimeOffset pubDate, string? description = null, string? imageUrl = null, TimeSpan? duration = null)
    {
        Id = id;
        Title = title;
        AudioUrl = audioUrl;
        PubDate = pubDate;
        Description = description;
        ImageUrl = imageUrl;
        Duration = duration;
    }

    public string Id { get; }

    public string Title { get; }

    public string AudioUrl { get; }

    public DateTimeOffset PubDate { get; }

    public string? Description { get; }

    public string? ImageUrl { get; }

    public TimeSpan? Duration { get; }
}
