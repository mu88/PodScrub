namespace PodScrub.Domain;

public interface IEpisodeDownloader
{
    Task<string> DownloadEpisodeAsync(string url, string targetDirectory, CancellationToken cancellationToken);
}
