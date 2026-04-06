using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PodScrub.Domain;

namespace PodScrub.Application;

public partial class SyncFeedUseCase
{
    private readonly IRssFeedReader _rssFeedReader;
    private readonly ProcessEpisodeUseCase _processEpisode;
    private readonly IOptions<PodScrubOptions> _options;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<SyncFeedUseCase> _logger;

    public SyncFeedUseCase(
        IRssFeedReader rssFeedReader,
        ProcessEpisodeUseCase processEpisode,
        IOptions<PodScrubOptions> options,
        IFileSystem fileSystem,
        ILogger<SyncFeedUseCase> logger)
    {
        _rssFeedReader = rssFeedReader;
        _processEpisode = processEpisode;
        _options = options;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Episode>> ExecuteAsync(Feed feed, string outputDirectory, IReadOnlyList<Episode> existingEpisodes, CancellationToken cancellationToken)
    {
        LogSyncingFeed(feed.Name);

        var feedItems = await _rssFeedReader.ReadFeedItemsAsync(feed.Url, cancellationToken);

        var itemsToProcess = ApplyProcessLatestFilter(feedItems, feed);

        var existingIds = existingEpisodes.Select(episode => episode.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newItems = itemsToProcess.Where(item => !existingIds.Contains(item.Id)).ToList();

        LogNewEpisodesFound(newItems.Count, feed.Name);

        var processedEpisodes = new List<Episode>(existingEpisodes);

        foreach (var item in newItems)
        {
            var episode = new Episode(item.Id, feed.Name, item.Title, item.AudioUrl, item.PubDate)
            {
                Description = item.Description,
                ImageUrl = item.ImageUrl,
                Duration = item.Duration,
            };

            try
            {
                var processedEpisode = await _processEpisode.ExecuteAsync(episode, feed.Jingles, outputDirectory, cancellationToken);
                processedEpisodes.Add(processedEpisode);
            }
            catch (Exception ex)
            {
                LogProcessingFailed(ex, item.Title);
            }
        }

        return ApplyRetentionPolicy(processedEpisodes);
    }

    internal static IReadOnlyList<FeedItem> ApplyProcessLatestFilter(IReadOnlyList<FeedItem> feedItems, Feed feed)
    {
        if (feed.ProcessLatest is null or <= 0)
        {
            return feedItems;
        }

        return feedItems
            .OrderByDescending(item => item.PubDate)
            .Take(feed.ProcessLatest.Value)
            .ToList();
    }

    internal IReadOnlyList<Episode> ApplyRetentionPolicy(List<Episode> episodes)
    {
        var maxEpisodes = _options.Value.MaxEpisodesPerFeed;
        if (maxEpisodes <= 0 || episodes.Count <= maxEpisodes)
        {
            return episodes;
        }

        var sorted = episodes.OrderByDescending(episode => episode.PubDate).ToList();
        var toKeep = sorted.Take(maxEpisodes).ToList();
        var toEvict = sorted.Skip(maxEpisodes).ToList();

        foreach (var evicted in toEvict)
        {
            CleanupEvictedEpisode(evicted);
        }

        return toKeep;
    }

    private void CleanupEvictedEpisode(Episode episode)
    {
        if (episode.ProcessedAudioPath is null)
        {
            return;
        }

        try
        {
            if (_fileSystem.FileExists(episode.ProcessedAudioPath))
            {
                _fileSystem.DeleteFile(episode.ProcessedAudioPath);
                LogEpisodeEvicted(episode.Title, episode.ProcessedAudioPath);
            }
        }
        catch (IOException ex)
        {
            LogEvictionFailed(ex, episode.Title);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Syncing feed '{name}'")]
    private partial void LogSyncingFeed(string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {count} new episode(s) in feed '{name}'")]
    private partial void LogNewEpisodesFound(int count, string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process episode '{title}'")]
    private partial void LogProcessingFailed(Exception ex, string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "Evicted episode '{title}', deleted '{path}'")]
    private partial void LogEpisodeEvicted(string title, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete evicted episode '{title}'")]
    private partial void LogEvictionFailed(IOException ex, string title);
}
