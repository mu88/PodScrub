namespace PodScrub.Domain;

public interface IRssFeedReader
{
    Task<FeedMetadata> ReadFeedMetadataAsync(string feedUrl, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeedItem>> ReadFeedItemsAsync(string feedUrl, CancellationToken cancellationToken);
}
