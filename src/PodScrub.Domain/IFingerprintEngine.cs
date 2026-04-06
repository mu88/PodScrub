namespace PodScrub.Domain;

public interface IFingerprintEngine
{
    Task StoreJingleFingerprintAsync(string jingleAudioPath, string jingleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TimeSpan>> FindJingleMatchesAsync(string episodeAudioPath, string jingleId, CancellationToken cancellationToken);
}
