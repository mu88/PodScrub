using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using PodScrub.Application;
using PodScrub.Domain;

namespace PodScrub.Api;

[ExcludeFromCodeCoverage]
public partial class FeedPollingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<Feed> _feeds;
    private readonly IOptions<PodScrubOptions> _options;
    private readonly IFingerprintEngine _fingerprintEngine;
    private readonly ConcurrentDictionary<string, List<Episode>> _episodeStore;
    private readonly ILogger<FeedPollingBackgroundService> _logger;

    public FeedPollingBackgroundService(
        IServiceProvider serviceProvider,
        IReadOnlyList<Feed> feeds,
        IOptions<PodScrubOptions> options,
        IFingerprintEngine fingerprintEngine,
        ConcurrentDictionary<string, List<Episode>> episodeStore,
        ILogger<FeedPollingBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _feeds = feeds;
        _options = options;
        _fingerprintEngine = fingerprintEngine;
        _episodeStore = episodeStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogPollingStarted(_options.Value.PollIntervalMinutes);

        await InitializeJinglesAsync(stoppingToken);

        await PollFeedsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.Value.PollIntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollFeedsAsync(stoppingToken);
        }
    }

    private async Task InitializeJinglesAsync(CancellationToken cancellationToken)
    {
        LogInitializingJingles();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var extractJingle = scope.ServiceProvider.GetRequiredService<ExtractJingleUseCase>();

        var jinglesDir = Path.Combine(_options.Value.DataPath, "jingles");
        Directory.CreateDirectory(jinglesDir);

        var extractedSourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feed in _feeds)
        {
            foreach (var jingle in feed.Jingles)
            {
                try
                {
                    var jinglePath = await extractJingle.ExecuteAsync(jingle, jinglesDir, cancellationToken);
                    var jingleId = Path.GetFileNameWithoutExtension(jinglePath);
                    await _fingerprintEngine.StoreJingleFingerprintAsync(jinglePath, jingleId, cancellationToken);
                    LogJingleInitialized(jingle.Type, feed.Name);

                    // Track source file so it can be cleaned up after all jingles are extracted
                    extractedSourceFiles.Add(Path.Combine(jinglesDir, $"{ExtractJingleUseCase.CreateUrlHash(jingle.SourceEpisodeUrl)}.mp3"));
                }
                catch (Exception ex)
                {
                    LogJingleInitializationFailed(ex, jingle.Type, feed.Name);
                }
            }
        }

        // Clean up source episode files now that all jingles have been extracted
        foreach (var sourceFile in extractedSourceFiles)
        {
            CleanupSourceFile(sourceFile);
        }
    }

    private async Task PollFeedsAsync(CancellationToken cancellationToken)
    {
        LogPollCycleStarted();

        foreach (var feed in _feeds)
        {
            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var syncFeed = scope.ServiceProvider.GetRequiredService<SyncFeedUseCase>();

                var existingEpisodes = _episodeStore.GetValueOrDefault(feed.Name, []);
                var outputDirectory = Path.Combine(_options.Value.DataPath, feed.Name);
                Directory.CreateDirectory(outputDirectory);

                var updatedEpisodes = await syncFeed.ExecuteAsync(feed, outputDirectory, existingEpisodes, cancellationToken);
                _episodeStore[feed.Name] = updatedEpisodes.ToList();

                LogFeedSynced(feed.Name, updatedEpisodes.Count);
            }
            catch (Exception ex)
            {
                LogPollError(ex, feed.Name);
            }
        }
    }

    private void CleanupSourceFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException ex)
        {
            LogSourceCleanupFailed(ex, filePath);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing jingle fingerprints")]
    private partial void LogInitializingJingles();

    [LoggerMessage(Level = LogLevel.Information, Message = "Jingle {type} for feed '{name}' initialized")]
    private partial void LogJingleInitialized(JingleType type, string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to initialize jingle {type} for feed '{name}'")]
    private partial void LogJingleInitializationFailed(Exception ex, JingleType type, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feed polling service started, polling every {interval} minute(s)")]
    private partial void LogPollingStarted(int interval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting feed poll cycle")]
    private partial void LogPollCycleStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Feed '{name}' synced, {count} episode(s) total")]
    private partial void LogFeedSynced(string name, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error polling feed '{name}'")]
    private partial void LogPollError(Exception ex, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete source episode file '{path}'")]
    private partial void LogSourceCleanupFailed(IOException ex, string path);
}