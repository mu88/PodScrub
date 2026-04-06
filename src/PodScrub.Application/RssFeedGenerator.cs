using System.Xml.Linq;
using PodScrub.Domain;

namespace PodScrub.Application;

public static class RssFeedGenerator
{
    public static string GenerateFeed(FeedMetadata metadata, IReadOnlyList<Episode> episodes, string baseUrl, string feedName)
    {
        var itunesNs = XNamespace.Get("http://www.itunes.com/dtds/podcast-1.0.dtd");

        var channel = new XElement("channel",
            new XElement("title", metadata.Title),
            new XElement("description", metadata.Description),
            new XElement("link", metadata.Link));

        if (metadata.ImageUrl is not null)
        {
            channel.Add(new XElement(itunesNs + "image", new XAttribute("href", metadata.ImageUrl)));
        }

        foreach (var episode in episodes.OrderByDescending(episode => episode.PubDate))
        {
            var audioUrl = episode.IsProcessed
                ? $"{baseUrl.TrimEnd('/')}/podscrub/audio/{episode.Id}"
                : episode.OriginalAudioUrl;

            var displayTitle = episode.SegmentsRemoved > 0
                ? $"{episode.Title} [SCRUBBED]"
                : episode.Title;

            var item = new XElement("item",
                new XElement("title", displayTitle),
                new XElement("pubDate", episode.PubDate.ToString("R")),
                new XElement("guid", episode.Id),
                new XElement("enclosure",
                    new XAttribute("url", audioUrl),
                    new XAttribute("type", "audio/mpeg")));

            if (episode.Description is not null)
            {
                item.Add(new XElement("description", episode.Description));
                item.Add(new XElement(itunesNs + "summary", episode.Description));
            }

            if (episode.ImageUrl is not null)
            {
                item.Add(new XElement(itunesNs + "image", new XAttribute("href", episode.ImageUrl)));
            }

            if (episode.Duration is not null)
            {
                item.Add(new XElement(itunesNs + "duration", FormatDuration(episode.Duration.Value)));
            }

            channel.Add(item);
        }

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "itunes", itunesNs),
            channel);

        return new XDocument(new XDeclaration("1.0", "utf-8", null), rss).ToString();
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return totalHours > 0
            ? $"{totalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}
