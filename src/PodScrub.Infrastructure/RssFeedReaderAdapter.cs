using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using CodeHollow.FeedReader;
using PodScrub.Domain;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public class RssFeedReaderAdapter : IRssFeedReader
{
    private readonly HttpClient _httpClient;

    public RssFeedReaderAdapter(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<FeedMetadata> ReadFeedMetadataAsync(string feedUrl, CancellationToken cancellationToken)
    {
        var feedContent = await _httpClient.GetStringAsync(feedUrl, cancellationToken);
        var feed = FeedReader.ReadFromString(feedContent);

        return new FeedMetadata(
            feed.Title ?? string.Empty,
            feed.Description ?? string.Empty,
            feed.ImageUrl,
            feed.Link ?? feedUrl);
    }

    public async Task<IReadOnlyList<Domain.FeedItem>> ReadFeedItemsAsync(string feedUrl, CancellationToken cancellationToken)
    {
        var feedContent = await _httpClient.GetStringAsync(feedUrl, cancellationToken);
        var feed = FeedReader.ReadFromString(feedContent);

        return feed.Items
            .Where(item => GetEnclosureUrl(item) is not null)
            .Select(item => new Domain.FeedItem(
                item.Id ?? item.Link ?? Guid.NewGuid().ToString("N"),
                item.Title ?? string.Empty,
                GetEnclosureUrl(item)!,
                item.PublishingDate ?? DateTimeOffset.MinValue,
                item.Description,
                GetEpisodeImageUrl(item),
                ParseDuration(item)))
            .ToList();
    }

    private static string? GetEpisodeImageUrl(CodeHollow.FeedReader.FeedItem item)
    {
        var itunesNs = XNamespace.Get("http://www.itunes.com/dtds/podcast-1.0.dtd");
        var imageElement = item.SpecificItem?.Element?.Element(itunesNs + "image");
        return imageElement?.Attribute("href")?.Value;
    }

    private static TimeSpan? ParseDuration(CodeHollow.FeedReader.FeedItem item)
    {
        var itunesNs = XNamespace.Get("http://www.itunes.com/dtds/podcast-1.0.dtd");
        var durationValue = item.SpecificItem?.Element?.Element(itunesNs + "duration")?.Value;

        if (string.IsNullOrWhiteSpace(durationValue))
        {
            return null;
        }

        // Plain number → seconds (most common for podcast feeds)
        if (long.TryParse(durationValue, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        // HH:MM:SS or MM:SS format
        if (TimeSpan.TryParse(durationValue, System.Globalization.CultureInfo.InvariantCulture, out var timeSpan))
        {
            return timeSpan;
        }

        return null;
    }

    private static string? GetEnclosureUrl(CodeHollow.FeedReader.FeedItem item)
    {
        var enclosureElement = item.SpecificItem?.Element?.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "enclosure", StringComparison.OrdinalIgnoreCase));

        return enclosureElement?.Attribute("url")?.Value;
    }
}
