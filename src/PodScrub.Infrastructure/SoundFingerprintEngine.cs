using System.Diagnostics.CodeAnalysis;
using PodScrub.Domain;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public sealed class SoundFingerprintEngine : IFingerprintEngine
{
    private readonly InMemoryModelService _modelService;
    private readonly IAudioService _audioService;

    public SoundFingerprintEngine()
    {
        _modelService = new InMemoryModelService();
        _audioService = new SoundFingerprintingAudioService();
    }

    public async Task StoreJingleFingerprintAsync(string jingleAudioPath, string jingleId, CancellationToken cancellationToken)
    {
        var hashes = await FingerprintCommandBuilder.Instance
            .BuildFingerprintCommand()
            .From(jingleAudioPath)
            .UsingServices(_audioService)
            .Hash();

        _modelService.Insert(new TrackInfo(jingleId, jingleId, jingleId), hashes);
    }

    public async Task<IReadOnlyList<TimeSpan>> FindJingleMatchesAsync(string episodeAudioPath, string jingleId, CancellationToken cancellationToken)
    {
        var result = await QueryCommandBuilder.Instance
            .BuildQueryCommand()
            .From(episodeAudioPath)
            .UsingServices(_modelService, _audioService)
            .Query();

        return result.ResultEntries
            .Where(entry => string.Equals(entry.TrackId, jingleId, StringComparison.Ordinal))
            .Where(entry => entry.Audio is not null)
            .Select(entry => TimeSpan.FromSeconds(entry.Audio!.QueryMatchStartsAt))
            .OrderBy(timestamp => timestamp)
            .ToList();
    }
}
