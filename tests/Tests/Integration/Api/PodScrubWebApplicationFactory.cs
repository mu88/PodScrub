using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using PodScrub.Domain;

namespace Tests.Integration.Api;

internal class PodScrubWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyList<Feed> _feeds;
    private readonly IRssFeedReader _rssFeedReader;
    private readonly ConcurrentDictionary<string, List<Episode>> _episodeStore;

    public PodScrubWebApplicationFactory(
        IReadOnlyList<Feed>? feeds = null,
        IRssFeedReader? rssFeedReader = null,
        ConcurrentDictionary<string, List<Episode>>? episodeStore = null)
    {
        _feeds = feeds ?? [];
        _rssFeedReader = rssFeedReader ?? Substitute.For<IRssFeedReader>();
        _episodeStore = episodeStore ?? new ConcurrentDictionary<string, List<Episode>>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IReadOnlyList<Feed>>();
            services.AddSingleton(_feeds);

            services.RemoveAll<IRssFeedReader>();
            services.AddSingleton(_rssFeedReader);

            services.RemoveAll<ConcurrentDictionary<string, List<Episode>>>();
            services.AddSingleton(_episodeStore);

            services.RemoveAll<IFingerprintEngine>();
            services.AddSingleton(Substitute.For<IFingerprintEngine>());

            services.RemoveAll<IAudioProcessor>();
            services.AddSingleton(Substitute.For<IAudioProcessor>());

            services.RemoveAll<IEpisodeDownloader>();
            services.AddSingleton(Substitute.For<IEpisodeDownloader>());

            services.RemoveAll<IFileSystem>();
            var fileSystemMock = Substitute.For<IFileSystem>();
            fileSystemMock.FileExists(Arg.Any<string>()).Returns(callInfo => File.Exists(callInfo.Arg<string>()));
            services.AddSingleton(fileSystemMock);
        });
    }
}
