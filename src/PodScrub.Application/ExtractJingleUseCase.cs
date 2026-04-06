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
        LogExtractingJingle(jingle.SourceEpisodeUrl, jingle.TimestampStart, jingle.TimestampEnd);

        var episodePath = await _episodeDownloader.DownloadEpisodeAsync(jingle.SourceEpisodeUrl, jinglesDirectory, cancellationToken);

        var jingleFileName = $"jingle_{jingle.Type}_{Guid.NewGuid():N}.wav";
        var jingleOutputPath = Path.Combine(jinglesDirectory, jingleFileName);

        await _audioProcessor.ExtractClipAsync(episodePath, jingle.TimestampStart, jingle.TimestampEnd, jingleOutputPath, cancellationToken);

        CleanupSourceEpisode(episodePath);

        jingle.SetAudioFilePath(jingleOutputPath);

        LogJingleExtracted(jingleOutputPath);

        return jingleOutputPath;
    }

    private void CleanupSourceEpisode(string filePath)
    {
        try
        {
            if (_fileSystem.FileExists(filePath))
            {
                _fileSystem.DeleteFile(filePath);
            }
        }
        catch (IOException ex)
        {
            LogCleanupFailed(ex, filePath);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracting jingle from {url} ({start} - {end})")]
    private partial void LogExtractingJingle(string url, TimeSpan start, TimeSpan end);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jingle extracted to {path}")]
    private partial void LogJingleExtracted(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete source episode '{path}'")]
    private partial void LogCleanupFailed(IOException ex, string path);
}
