using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using CodeHollow.FeedReader;
using Microsoft.Extensions.Logging;
using PodScrub.Domain;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public partial class RssFeedReaderAdapter : IRssFeedReader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RssFeedReaderAdapter> _logger;

    public RssFeedReaderAdapter(HttpClient httpClient, ILogger<RssFeedReaderAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

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
            .Select(item =>
            {
                var id = item.Id ?? item.Link;
                if (id is null)
                {
                    LogMissingItemId(feedUrl);
                    id = Guid.NewGuid().ToString("N");
                }

                return new Domain.FeedItem(
                    id,
                    item.Title ?? string.Empty,
                    GetEnclosureUrl(item)!,
                    item.PublishingDate ?? DateTimeOffset.MinValue,
                    item.Description,
                    GetEpisodeImageUrl(item),
                    ParseDuration(item));
            })
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feed item from '{feedUrl}' has no id or link — assigning a random GUID. This item will be reprocessed on every poll cycle.")]
    private partial void LogMissingItemId(string feedUrl);
}
