using System.Diagnostics.CodeAnalysis;
using PodScrub.Domain;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public class EpisodeDownloader : IEpisodeDownloader
{
    private readonly HttpClient _httpClient;

    public EpisodeDownloader(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<string> DownloadEpisodeAsync(string url, string targetDirectory, string fileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        var targetPath = Path.Combine(targetDirectory, fileName);

        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var fileStream = File.Create(targetPath);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await contentStream.CopyToAsync(fileStream, cancellationToken);

        return targetPath;
    }
}
