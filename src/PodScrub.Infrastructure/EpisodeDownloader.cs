using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using PodScrub.Domain;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public class EpisodeDownloader : IEpisodeDownloader
{
    private readonly HttpClient _httpClient;

    public EpisodeDownloader(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<string> DownloadEpisodeAsync(string url, string targetDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        var uri = new Uri(url);
        var extension = Path.GetExtension(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp3";
        }

        // Use URL hash as filename to avoid collisions from CDNs using generic names like "audio.mp3"
        var urlHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        var fileName = $"{urlHash}{extension}";
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
