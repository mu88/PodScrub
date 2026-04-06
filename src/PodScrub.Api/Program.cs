using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using mu88.Shared.OpenTelemetry;
using PodScrub.Application;
using PodScrub.Domain;
using PodScrub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureOpenTelemetry("podscrub", builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.FFFK ";
    options.SingleLine = true;
});

builder.Services.Configure<PodScrubOptions>(builder.Configuration.GetSection(PodScrubOptions.SectionName));

builder.Services.AddSingleton<ConcurrentDictionary<string, List<Episode>>>();

var podScrubOptions = builder.Configuration.GetSection(PodScrubOptions.SectionName).Get<PodScrubOptions>() ?? new PodScrubOptions();
var feeds = FeedOptionsMapper.MapToFeeds(podScrubOptions.Feeds);
var allowedUrls = feeds
    .Select(feed => feed.Url)
    .Concat(feeds.SelectMany(feed => feed.Jingles.Select(jingle => jingle.SourceEpisodeUrl)))
    .ToList();

builder.Services.AddHttpClient("podcast")
    .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
    {
        var logger = serviceProvider.GetRequiredService<ILogger<AllowListHttpMessageHandler>>();
        return new AllowListHttpMessageHandler(allowedUrls, logger);
    });

builder.Services.AddSingleton<IReadOnlyList<Feed>>(feeds);
builder.Services.AddSingleton<IFingerprintEngine, SoundFingerprintEngine>();
builder.Services.AddSingleton<IAudioProcessor, AudioProcessor>();
builder.Services.AddSingleton<IFileSystem, FileSystem>();
builder.Services.AddSingleton<IRssFeedReader>(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    return new RssFeedReaderAdapter(httpClientFactory.CreateClient("podcast"));
});
builder.Services.AddSingleton<IEpisodeDownloader>(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    return new EpisodeDownloader(httpClientFactory.CreateClient("podcast"));
});

builder.Services.AddTransient<ExtractJingleUseCase>();
builder.Services.AddTransient<DetectInterludesUseCase>();
builder.Services.AddTransient<ProcessEpisodeUseCase>();
builder.Services.AddTransient<SyncFeedUseCase>();

builder.Services.AddHostedService<FeedPollingBackgroundService>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UsePathBase("/podscrub");

app.MapGet("/feed/{name}/rss.xml", async (
    string name,
    IReadOnlyList<Feed> configuredFeeds,
    IRssFeedReader rssFeedReader,
    ConcurrentDictionary<string, List<Episode>> episodeStore,
    IOptions<PodScrubOptions> options,
    CancellationToken cancellationToken) =>
{
    var feed = configuredFeeds.FirstOrDefault(feed => string.Equals(feed.Name, name, StringComparison.OrdinalIgnoreCase));
    if (feed is null)
    {
        return Results.NotFound($"Feed '{name}' not found.");
    }

    var metadata = await rssFeedReader.ReadFeedMetadataAsync(feed.Url, cancellationToken);
    var episodes = episodeStore.GetValueOrDefault(feed.Name, []);

    var rssXml = RssFeedGenerator.GenerateFeed(metadata, episodes, options.Value.BaseUrl, feed.Name);
    return Results.Content(rssXml, "application/rss+xml");
}).WithName("GetFeed");

app.MapGet("/audio/{episodeId}", (
    string episodeId,
    ConcurrentDictionary<string, List<Episode>> episodeStore) =>
{
    var episode = episodeStore.Values
        .SelectMany(episodes => episodes)
        .FirstOrDefault(episode => string.Equals(episode.Id, episodeId, StringComparison.OrdinalIgnoreCase));

    if (episode?.ProcessedAudioPath is null || !File.Exists(episode.ProcessedAudioPath))
    {
        return Results.NotFound("Episode audio not found.");
    }

    return Results.File(episode.ProcessedAudioPath, "audio/mpeg");
}).WithName("GetAudio");

app.MapHealthChecks("/healthz");

await app.RunAsync();

[ExcludeFromCodeCoverage]
[SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1106:Code should not contain empty statements", Justification = "Necessary for code coverage")]
[SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "S1118", Justification = "Necessary for code coverage")]
[SuppressMessage("ASP", "ASP0027:Using public partial class Program is no longer required", Justification = "StyleCop SA1205 requires access modifier on partial types")]
public partial class Program;
