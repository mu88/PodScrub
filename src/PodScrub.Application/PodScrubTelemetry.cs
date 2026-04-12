using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PodScrub.Application;

internal static class PodScrubTelemetry
{
    private const string SourceName = "podscrub";

    internal static readonly ActivitySource ActivitySource = new(SourceName);

    internal static readonly Meter Meter = new(SourceName);

    internal static readonly Counter<int> EpisodesProcessed =
        Meter.CreateCounter<int>("podscrub.episodes.processed", description: "Number of episodes processed");

    internal static readonly Counter<int> SegmentsRemoved =
        Meter.CreateCounter<int>("podscrub.segments.removed", description: "Number of ad segments removed from episodes");

    internal static readonly Counter<int> JinglesExtracted =
        Meter.CreateCounter<int>("podscrub.jingles.extracted", description: "Number of jingles extracted");
}
