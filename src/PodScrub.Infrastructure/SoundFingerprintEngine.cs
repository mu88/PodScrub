using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using PodScrub.Domain;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public sealed partial class SoundFingerprintEngine : IFingerprintEngine
{
    private readonly InMemoryModelService _modelService;
    private readonly IAudioService _audioService;
    private readonly ILogger<SoundFingerprintEngine> _logger;

    public SoundFingerprintEngine(ILogger<SoundFingerprintEngine> logger)
    {
        _modelService = new InMemoryModelService();
        _audioService = new SoundFingerprintingAudioService();
        _logger = logger;
    }

    public async Task StoreJingleFingerprintAsync(string jingleAudioPath, string jingleId, CancellationToken cancellationToken)
    {
        var hashes = await FingerprintCommandBuilder.Instance
            .BuildFingerprintCommand()
            .From(jingleAudioPath)
            .UsingServices(_audioService)
            .Hash();

        LogJingleFingerprinted(jingleId, hashes.Count);
        _modelService.Insert(new TrackInfo(jingleId, jingleId, jingleId), hashes);
    }

    public async Task<IReadOnlyList<TimeSpan>> FindJingleMatchesAsync(string episodeAudioPath, string jingleId, CancellationToken cancellationToken)
    {
        var result = await QueryCommandBuilder.Instance
            .BuildQueryCommand()
            .From(episodeAudioPath)
            .WithQueryConfig(config =>
            {
                config.Audio.ThresholdVotes = 2;
                config.Audio.MaxTracksToReturn = 100;
                return config;
            })
            .UsingServices(_modelService, _audioService)
            .Query();

        var entries = result.ResultEntries.ToList();
        LogQueryResults(episodeAudioPath, entries.Count);

        foreach (var entry in entries)
        {
            LogResultEntry(
                entry.TrackId,
                entry.Audio?.Confidence ?? -1,
                entry.Audio?.QueryLength ?? -1,
                entry.Audio?.QueryMatchStartsAt ?? -1);
        }

        return entries
            .Where(entry => string.Equals(entry.TrackId, jingleId, StringComparison.Ordinal))
            .Where(entry => entry.Audio is not null)
            .Where(entry => entry.Audio!.Confidence >= 0.4)
            .Select(entry => TimeSpan.FromSeconds(entry.Audio!.QueryMatchStartsAt))
            .OrderBy(timestamp => timestamp)
            .ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored {count} sub-fingerprint(s) for jingle '{jingleId}'")]
    private partial void LogJingleFingerprinted(string jingleId, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Query against '{path}' returned {count} result entries")]
    private partial void LogQueryResults(string path, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "  Entry: TrackId={trackId}, Confidence={confidence:F3}, QueryLength={queryLength:F1}s, MatchStartsAt={matchStartsAt:F1}s")]
    private partial void LogResultEntry(string trackId, double confidence, double queryLength, double matchStartsAt);
}
