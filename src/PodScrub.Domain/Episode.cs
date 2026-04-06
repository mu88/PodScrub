namespace PodScrub.Domain;

public class Episode
{
    public Episode(string id, string feedName, string title, string originalAudioUrl, DateTimeOffset pubDate)
    {
        Id = id;
        FeedName = feedName;
        Title = title;
        OriginalAudioUrl = originalAudioUrl;
        PubDate = pubDate;
    }

    public string Id { get; }

    public string FeedName { get; }

    public string Title { get; }

    public string OriginalAudioUrl { get; }

    public DateTimeOffset PubDate { get; }

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? ProcessedAudioPath { get; private set; }

    public bool IsProcessed => ProcessedAudioPath is not null;

    public int SegmentsRemoved { get; private set; }

    public void MarkProcessed(string processedAudioPath, int segmentsRemoved = 0)
    {
        ProcessedAudioPath = processedAudioPath;
        SegmentsRemoved = segmentsRemoved;
    }
}
