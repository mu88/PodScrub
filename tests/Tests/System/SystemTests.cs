using System.Diagnostics.CodeAnalysis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using FluentAssertions;
using mu88.Shared.Testing.Docker;
using mu88.Shared.Testing.SystemTests;
using NUnit.Framework;

namespace Tests.System;

[TestFixture]
[Category("System")]
public class SystemTests : SystemTestsBase
{
    [SuppressMessage("NUnit1032", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method", Justification = "Disposed via CleanupAdditionalResourcesAsync, called from the base class's [TearDown]-annotated method.")]
    private IContainer? _feedServer;

    [SuppressMessage("NUnit1032", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method", Justification = "Disposed via CleanupAdditionalResourcesAsync, called from the base class's [TearDown]-annotated method.")]
    private INetwork? _network;

    protected override string SubPath => "/podscrub";

    protected override TimeSpan Timeout => TimeSpan.FromMinutes(5);

    [Test]
    public async Task AppRunningInDocker_ShouldBeHealthy()
    {
        // Arrange
        var containerImageTag = DockerImageBuilder.GenerateContainerImageTag();
        await BuildDockerImageAsync(containerImageTag, CancellationToken);
        Container = await StartAppInContainerAsync(containerImageTag, CancellationToken);

        // Act & Assert
        await LogsShouldNotContainWarningsAsync(CancellationToken);
        await HealthCheckShouldSucceedAsync(CancellationToken);
    }

    [Test]
    public async Task AppRunningInDocker_ShouldProcessPodcastFeed()
    {
        // Arrange
        var testDataDir = Path.Combine(Path.GetTempPath(), $"podscrub-system-test-{Guid.NewGuid()}");
        const string feedServerAlias = "feed-server";

        try
        {
            var containerImageTag = DockerImageBuilder.GenerateContainerImageTag();
            await BuildDockerImageAsync(containerImageTag, CancellationToken);

            _network = new NetworkBuilder().Build();
            await _network.CreateAsync(CancellationToken);

            TestAudioGenerator.WriteTestFiles(testDataDir, $"http://{feedServerAlias}");

            _feedServer = new ContainerBuilder("nginx:alpine")
                .WithNetwork(_network)
                .WithNetworkAliases(feedServerAlias)
                .WithPortBinding(80, true)
                .WithResourceMapping(new DirectoryInfo(testDataDir), "/usr/share/nginx/html")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPath("/feed.rss").ForPort(80)))
                .Build();
            await _feedServer.StartAsync(CancellationToken);

            var appContainer = BuildAppContainerWithFeed(_network, containerImageTag, feedServerAlias);
            await appContainer.StartAsync(CancellationToken);
            Container = appContainer;

            // Act — wait for PodScrub to poll and sync the feed (initial poll is immediate on startup)
            var feedResponse = await WaitForFeedSyncAsync(HttpClient, "test-podcast", CancellationToken);

            // Assert
            await HealthCheckShouldSucceedAsync(CancellationToken);

            feedResponse.Should().NotBeNull("feed should be available after sync");
            feedResponse.Should().Contain("<title>Test Podcast", "feed should contain the podcast title");
            feedResponse.Should().Contain("Test Episode 1", "feed should contain the episode");
            feedResponse.Should().Contain("/podscrub/audio/", "episode should have a PodScrub audio URL");

            // Check container logs for successful processing
            (string stdout, string stderr) = await Container.GetLogsAsync(ct: CancellationToken);
            var allLogs = stdout + stderr;
            Console.WriteLine($"Container logs:{Environment.NewLine}{allLogs}");

            allLogs.Should().Contain("Initializing jingle fingerprints", "jingle initialization should have started");
            allLogs.Should().Contain("synced", "feed sync should have completed");
            allLogs.Should().NotContain("Error polling feed", "no feed polling errors should occur");
        }
        finally
        {
            if (Directory.Exists(testDataDir))
            {
                Directory.Delete(testDataDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Cleans up the additional <see cref="_feedServer"/> container and <see cref="_network"/> created by
    /// <see cref="AppRunningInDocker_ShouldProcessPodcastFeed"/>, beyond the base class's own <see cref="Container"/>
    /// cleanup.
    /// </summary>
    protected override async Task CleanupAdditionalResourcesAsync(CancellationToken cancellationToken)
    {
        if (_feedServer is not null)
        {
            await _feedServer.StopAsync(cancellationToken);
            await _feedServer.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DeleteAsync(cancellationToken);
            await _network.DisposeAsync();
        }
    }

    private static async Task<string?> WaitForFeedSyncAsync(HttpClient httpClient, string feedName, CancellationToken cancellationToken)
    {
        // Poll the feed endpoint until the episode appears (max 120 seconds)
        var timeout = DateTimeOffset.UtcNow.AddSeconds(120);
        while (DateTimeOffset.UtcNow < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await httpClient.GetAsync($"feed/{feedName}/rss.xml", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (content.Contains("Test Episode 1", StringComparison.Ordinal))
                    {
                        Console.WriteLine("Feed synced successfully");
                        return content;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Container may not be fully ready yet
            }

            await Task.Delay(2000, cancellationToken);
        }

        return null;
    }

    private static IContainer BuildAppContainerWithFeed(INetwork network, string containerImageTag, string feedServerAlias)
    {
        var jingleSourceUrl = $"http://{feedServerAlias}/jingle-source.wav";

        return new ContainerBuilder($"podscrub-api:{containerImageTag}")
            .WithNetwork(network)
            .WithEnvironment("PodScrub__BaseUrl", "http://localhost:8080")
            .WithEnvironment("PodScrub__DataPath", "/tmp/data")
            .WithEnvironment("PodScrub__PollIntervalMinutes", "60")
            .WithEnvironment("PodScrub__Feeds__0__Name", "test-podcast")
            .WithEnvironment("PodScrub__Feeds__0__Url", $"http://{feedServerAlias}/feed.rss")
            .WithEnvironment("PodScrub__Feeds__0__Jingles__0__Type", "InterludeStart")
            .WithEnvironment("PodScrub__Feeds__0__Jingles__0__Group", "main-interlude")
            .WithEnvironment("PodScrub__Feeds__0__Jingles__0__SourceEpisode", jingleSourceUrl)
            .WithEnvironment("PodScrub__Feeds__0__Jingles__0__TimestampStart", FormatTimestamp(TestAudioGenerator.JingleSourceInterludeStartBegin))
            .WithEnvironment("PodScrub__Feeds__0__Jingles__0__TimestampEnd", FormatTimestamp(TestAudioGenerator.JingleSourceInterludeStartEnd))
            .WithEnvironment("PodScrub__Feeds__0__Jingles__1__Type", "InterludeEnd")
            .WithEnvironment("PodScrub__Feeds__0__Jingles__1__Group", "main-interlude")
            .WithEnvironment("PodScrub__Feeds__0__Jingles__1__SourceEpisode", jingleSourceUrl)
            .WithEnvironment("PodScrub__Feeds__0__Jingles__1__TimestampStart", FormatTimestamp(TestAudioGenerator.JingleSourceInterludeEndBegin))
            .WithEnvironment("PodScrub__Feeds__0__Jingles__1__TimestampEnd", FormatTimestamp(TestAudioGenerator.JingleSourceInterludeEndEnd))
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Content root path: /app",
                    strategy => strategy.WithTimeout(TimeSpan.FromSeconds(30))))
            .Build();
    }

    private static string FormatTimestamp(TimeSpan timeSpan) => timeSpan.ToString(@"hh\:mm\:ss");

    private static async Task BuildDockerImageAsync(string containerImageTag, CancellationToken cancellationToken)
    {
        var rootDirectory = Directory.GetParent(Environment.CurrentDirectory)?.Parent?.Parent?.Parent?.Parent ?? throw new NullReferenceException();
        var apiProjectFile = Path.Join(rootDirectory.FullName, "src", "PodScrub.Api", "PodScrub.Api.csproj");
        await DockerImageBuilder.BuildAsync(apiProjectFile, containerImageTag, "podscrub-api", rootDirectory.FullName, cancellationToken);
    }

    private static async Task<IContainer> StartAppInContainerAsync(string containerImageTag, CancellationToken cancellationToken)
    {
        Console.WriteLine("Building and starting network");
        var network = new NetworkBuilder().Build();
        await network.CreateAsync(cancellationToken);
        Console.WriteLine("Network started");

        Console.WriteLine("Building and starting PodScrub container");
        var container = BuildAppContainer(network, containerImageTag);
        await container.StartAsync(cancellationToken);
        Console.WriteLine("PodScrub container started");

        return container;
    }

    private static IContainer BuildAppContainer(INetwork network, string containerImageTag)
        => new ContainerBuilder($"podscrub-api:{containerImageTag}")
            .WithNetwork(network)
            .WithEnvironment("PodScrub__BaseUrl", "http://localhost:8080")
            .WithEnvironment("PodScrub__DataPath", "/tmp/data")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Content root path: /app",
                    strategy => strategy.WithTimeout(TimeSpan.FromSeconds(30))))
            .Build();
}
