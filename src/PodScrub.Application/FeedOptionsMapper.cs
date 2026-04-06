using System.Globalization;
using PodScrub.Domain;

namespace PodScrub.Application;

public static class FeedOptionsMapper
{
    public static IReadOnlyList<Feed> MapToFeeds(IEnumerable<FeedOptions> feedOptions)
    {
        return feedOptions
            .Select(feedOption => new Feed(
                feedOption.Name,
                feedOption.Url,
                feedOption.Jingles
                    .Select(jingleOption => new Jingle(
                        ParseJingleType(jingleOption.Type),
                        jingleOption.SourceEpisode,
                        TimeSpan.Parse(jingleOption.TimestampStart, CultureInfo.InvariantCulture),
                        TimeSpan.Parse(jingleOption.TimestampEnd, CultureInfo.InvariantCulture),
                        string.IsNullOrWhiteSpace(jingleOption.Group) ? "default" : jingleOption.Group))
                    .ToList(),
                feedOption.ProcessLatest > 0 ? feedOption.ProcessLatest : null))
            .ToList();
    }

    internal static JingleType ParseJingleType(string value)
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.Parse<JingleType>(normalized, ignoreCase: true);
    }
}
