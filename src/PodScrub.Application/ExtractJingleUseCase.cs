using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PodScrub.Domain;

namespace PodScrub.Application;

public partial class ExtractJingleUseCase
{
    private readonly IEpisodeDownloader _episodeDownloader;
    private readonly IAudioProcessor _audioProcessor;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ExtractJingleUseCase> _logger;

    public ExtractJingleUseCase(
        IEpisodeDownloader episodeDownloader,
        IAudioProcessor audioProcessor,
        IFileSystem fileSystem,
        ILogger<ExtractJingleUseCase> logger)
    {
        _episodeDownloader = episodeDownloader;
        _audioProcessor = audioProcessor;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(Jingle jingle, string jinglesDirectory, CancellationToken cancellationToken)
    {
        using var activity = PodScrubTelemetry.ActivitySource.StartActivity("extract-jingle");
        activity?.SetTag("jingle.type", jingle.Type.ToString());

        LogExtractingJingle(jingle.SourceEpisodeUrl, jingle.TimestampStart, jingle.TimestampEnd);

        var jingleFileName = CreateDeterministicJingleFileName(jingle);
        var jingleOutputPath = Path.Combine(jinglesDirectory, jingleFileName);

        // Idempotent: skip extraction if jingle already exists from a previous run
        if (_fileSystem.FileExists(jingleOutputPath))
        {
            jingle.SetAudioFilePath(jingleOutputPath);
            LogJingleAlreadyExtracted(jingleOutputPath);
            return jingleOutputPath;
        }

        // Use URL hash as filename so multiple jingles from the same source episode reuse the download
        var sourceFileName = $"{CreateUrlHash(jingle.SourceEpisodeUrl)}.mp3";
        var episodePath = await _episodeDownloader.DownloadEpisodeAsync(jingle.SourceEpisodeUrl, jinglesDirectory, sourceFileName, cancellationToken);

        await _audioProcessor.ExtractClipAsync(episodePath, jingle.TimestampStart, jingle.TimestampEnd, jingleOutputPath, cancellationToken);

        jingle.SetAudioFilePath(jingleOutputPath);
        PodScrubTelemetry.JinglesExtracted.Add(1);
        LogJingleExtracted(jingleOutputPath);

        return jingleOutputPath;
    }

    public static string CreateUrlHash(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];

    private static string CreateDeterministicJingleFileName(Jingle jingle)
    {
        var key = $"{jingle.Type}|{jingle.SourceEpisodeUrl}|{jingle.TimestampStart.Ticks}|{jingle.TimestampEnd.Ticks}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return $"jingle_{jingle.Type}_{hash}.wav";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracting jingle from {url} ({start} - {end})")]
    private partial void LogExtractingJingle(string url, TimeSpan start, TimeSpan end);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jingle already extracted at {path}, skipping")]
    private partial void LogJingleAlreadyExtracted(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jingle extracted to {path}")]
    private partial void LogJingleExtracted(string path);
}
