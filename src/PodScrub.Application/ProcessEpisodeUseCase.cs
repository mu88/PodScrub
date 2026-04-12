using System.Text.Json;
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
        using var activity = PodScrubTelemetry.ActivitySource.StartActivity("process-episode");
        activity?.SetTag("episode.id", episode.Id);
        activity?.SetTag("episode.title", episode.Title);

        var processedDir = Path.Combine(outputDirectory, "processed");
        _fileSystem.CreateDirectory(processedDir);
        var processedPath = Path.Combine(processedDir, $"{episode.Id}.mp3");

        // Skip if already processed from a previous run (restart resilience)
        if (_fileSystem.FileExists(processedPath))
        {
            LogAlreadyProcessed(episode.Title);
            var segmentsRemoved = ReadSidecarSegments(processedPath);
            episode.MarkProcessed(processedPath, segmentsRemoved);
            return episode;
        }

        LogProcessingEpisode(episode.Title);

        var downloadDir = Path.Combine(outputDirectory, "downloads");
        _fileSystem.CreateDirectory(downloadDir);

        var episodePath = await _episodeDownloader.DownloadEpisodeAsync(episode.OriginalAudioUrl, downloadDir, $"{episode.Id}.mp3", cancellationToken);

        var wavPath = await _audioProcessor.ConvertToWavAsync(episodePath, downloadDir, cancellationToken);

        var segments = await _detectInterludes.ExecuteAsync(wavPath, jingles, cancellationToken);

        CleanupTemporaryFile(wavPath);

        if (segments.Count == 0)
        {
            LogNoSegmentsFound(episode.Title);
            _fileSystem.CopyFile(episodePath, processedPath);
            episode.MarkProcessed(processedPath);
            PodScrubTelemetry.EpisodesProcessed.Add(1);
            return episode;
        }

        await _audioProcessor.RemoveSegmentsAsync(episodePath, segments, processedPath, _options.Value.TransitionTonePath, cancellationToken);

        WriteSidecarSegments(processedPath, segments.Count);

        episode.MarkProcessed(processedPath, segments.Count);
        PodScrubTelemetry.EpisodesProcessed.Add(1);
        PodScrubTelemetry.SegmentsRemoved.Add(segments.Count);

        LogEpisodeProcessed(episode.Title, segments.Count);

        return episode;
    }

    private int ReadSidecarSegments(string processedPath)
    {
        var sidecarPath = Path.ChangeExtension(processedPath, ".json");
        if (!_fileSystem.FileExists(sidecarPath))
        {
            return 0;
        }

        try
        {
            var sidecar = JsonSerializer.Deserialize<EpisodeSidecar>(_fileSystem.ReadAllText(sidecarPath));
            return sidecar?.SegmentsRemoved ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private void WriteSidecarSegments(string processedPath, int segmentsRemoved)
    {
        var sidecarPath = Path.ChangeExtension(processedPath, ".json");
        _fileSystem.WriteAllText(sidecarPath, JsonSerializer.Serialize(new EpisodeSidecar(segmentsRemoved)));
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Episode '{title}' already processed, skipping")]
    private partial void LogAlreadyProcessed(string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "No interludes found in '{title}', using original audio")]
    private partial void LogNoSegmentsFound(string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "Episode '{title}' processed, {count} segment(s) removed")]
    private partial void LogEpisodeProcessed(string title, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted original download '{path}'")]
    private partial void LogOriginalDeleted(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete original download '{path}'")]
    private partial void LogCleanupFailed(IOException ex, string path);

    private sealed record EpisodeSidecar(int SegmentsRemoved);
}
