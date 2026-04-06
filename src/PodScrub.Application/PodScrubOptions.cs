using System.Diagnostics.CodeAnalysis;

namespace PodScrub.Application;

[ExcludeFromCodeCoverage]
public class PodScrubOptions
{
    public const string SectionName = "PodScrub";

    public string BaseUrl { get; set; } = "http://localhost:8080";

    public int PollIntervalMinutes { get; set; } = 60;

    public string DataPath { get; set; } = "./data";

    public int MaxEpisodesPerFeed { get; set; } = 50;

    public string? TransitionTonePath { get; set; }

    public List<FeedOptions> Feeds { get; set; } = [];
}

[ExcludeFromCodeCoverage]
public class FeedOptions
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public int ProcessLatest { get; set; }

    public List<JingleOptions> Jingles { get; set; } = [];
}

[ExcludeFromCodeCoverage]
public class JingleOptions
{
    public string Type { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    public string SourceEpisode { get; set; } = string.Empty;

    public string TimestampStart { get; set; } = string.Empty;

    public string TimestampEnd { get; set; } = string.Empty;
}
