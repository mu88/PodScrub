using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PodScrub.Domain;

namespace PodScrub.Application;

public partial class ProcessEpisodeUseCase
{
    private readonly DetectInterludesUseCase _detectInterludes;
    private readonly IEpisodeDownloader _episodeDownloader;
    private readonly IAudioProcessor _audioProcessor;
    private readonly IFileSystem _fileSystem;
    private readonly IOptions<PodScrubOptions> _options;
    private readonly ILogger<ProcessEpisodeUseCase> _logger;

    public ProcessEpisodeUseCase(
        DetectInterludesUseCase detectInterludes,
        IEpisodeDownloader episodeDownloader,
        IAudioProcessor audioProcessor,
        IFileSystem fileSystem,
        IOptions<PodScrubOptions> options,
        ILogger<ProcessEpisodeUseCase> logger)
    {
        _detectInterludes = detectInterludes;
        _episodeDownloader = episodeDownloader;
        _audioProcessor = audioProcessor;
        _fileSystem = fileSystem;
        _options = options;
        _logger = logger;
    }

    public virtual async Task<Episode> ExecuteAsync(Episode episode, IReadOnlyList<Jingle> jingles, string outputDirectory, CancellationToken cancellationToken)
    {
        LogProcessingEpisode(episode.Title);

        var downloadDir = Path.Combine(outputDirectory, "downloads");
        Directory.CreateDirectory(downloadDir);

        var episodePath = await _episodeDownloader.DownloadEpisodeAsync(episode.OriginalAudioUrl, downloadDir, cancellationToken);

        var wavPath = await _audioProcessor.ConvertToWavAsync(episodePath, downloadDir, cancellationToken);

        var segments = await _detectInterludes.ExecuteAsync(wavPath, jingles, cancellationToken);

        CleanupTemporaryFile(wavPath);

        if (segments.Count == 0)
        {
            LogNoSegmentsFound(episode.Title);
            episode.MarkProcessed(episodePath);
            return episode;
        }

        var processedDir = Path.Combine(outputDirectory, "processed");
        Directory.CreateDirectory(processedDir);

        var processedPath = Path.Combine(processedDir, $"{episode.Id}.mp3");
        await _audioProcessor.RemoveSegmentsAsync(episodePath, segments, processedPath, _options.Value.TransitionTonePath, cancellationToken);

        CleanupOriginalFile(episodePath);

        episode.MarkProcessed(processedPath, segments.Count);

        LogEpisodeProcessed(episode.Title, segments.Count);

        return episode;
    }

    private void CleanupOriginalFile(string filePath)
    {
        CleanupTemporaryFile(filePath);
    }

    private void CleanupTemporaryFile(string filePath)
    {
        try
        {
            if (_fileSystem.FileExists(filePath))
            {
                _fileSystem.DeleteFile(filePath);
                LogOriginalDeleted(filePath);
            }
        }
        catch (IOException ex)
        {
            LogCleanupFailed(ex, filePath);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing episode '{title}'")]
    private partial void LogProcessingEpisode(string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "No interludes found in '{title}', using original audio")]
    private partial void LogNoSegmentsFound(string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "Episode '{title}' processed, {count} segment(s) removed")]
    private partial void LogEpisodeProcessed(string title, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted original download '{path}'")]
    private partial void LogOriginalDeleted(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete original download '{path}'")]
    private partial void LogCleanupFailed(IOException ex, string path);
}
