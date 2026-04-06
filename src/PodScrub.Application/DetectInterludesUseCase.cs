using Microsoft.Extensions.Logging;
using PodScrub.Domain;

namespace PodScrub.Application;

public partial class DetectInterludesUseCase
{
    private readonly IFingerprintEngine _fingerprintEngine;
    private readonly ILogger<DetectInterludesUseCase> _logger;

    public DetectInterludesUseCase(IFingerprintEngine fingerprintEngine, ILogger<DetectInterludesUseCase> logger)
    {
        _fingerprintEngine = fingerprintEngine;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Interlude>> ExecuteAsync(string episodeAudioPath, IReadOnlyList<Jingle> jingles, CancellationToken cancellationToken)
    {
        var jinglesByGroup = jingles
            .Where(jingle => jingle.AudioFilePath is not null)
            .GroupBy(jingle => jingle.Group, StringComparer.OrdinalIgnoreCase);

        var allSegments = new List<Interlude>();

        foreach (var group in jinglesByGroup)
        {
            var interludeStartMatches = new List<TimeSpan>();
            var interludeEndMatches = new List<TimeSpan>();

            foreach (var jingle in group.Where(jingle => jingle.Type == JingleType.InterludeStart))
            {
                var jingleId = Path.GetFileNameWithoutExtension(jingle.AudioFilePath!);
                var matches = await _fingerprintEngine.FindJingleMatchesAsync(episodeAudioPath, jingleId, cancellationToken);
                interludeStartMatches.AddRange(matches);
            }

            foreach (var jingle in group.Where(jingle => jingle.Type == JingleType.InterludeEnd))
            {
                var jingleId = Path.GetFileNameWithoutExtension(jingle.AudioFilePath!);
                var matches = await _fingerprintEngine.FindJingleMatchesAsync(episodeAudioPath, jingleId, cancellationToken);
                interludeEndMatches.AddRange(matches);
            }

            allSegments.AddRange(PairInterludes(interludeStartMatches, interludeEndMatches));
        }

        LogDetectedInterludes(allSegments.Count, episodeAudioPath);

        return allSegments;
    }

    internal static IReadOnlyList<Interlude> PairInterludes(List<TimeSpan> starts, List<TimeSpan> ends)
    {
        starts.Sort();
        ends.Sort();

        var segments = new List<Interlude>();

        foreach (var start in starts)
        {
            var matchingEnd = ends.FirstOrDefault(end => end > start);
            if (matchingEnd != default)
            {
                segments.Add(new Interlude(start, matchingEnd));
                ends.Remove(matchingEnd);
            }
        }

        return segments;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected {count} interlude(s) in {path}")]
    private partial void LogDetectedInterludes(int count, string path);
}
