using System.Diagnostics.CodeAnalysis;
using CliWrap;
using CliWrap.Buffered;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using FluentAssertions;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Tests.System;

[TestFixture]
[Category("System")]
[SuppressMessage("ReSharper", "LocalizableElement", Justification = "Okay for me since it's just test output")]
public class SystemTests
{
    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationToken _cancellationToken;
    private DockerClient? _dockerClient;
    private IContainer? _container;
    private IContainer? _feedServer;
    private INetwork? _network;

    [SetUp]
    public void Setup()
    {
        _cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _cancellationToken = _cancellationTokenSource.Token;
        _dockerClient = new DockerClientConfiguration().CreateClient();
    }

    [TearDown]
    public async Task Teardown()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
        {
            return;
        }

        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Passed && _dockerClient is not null)
        {
            if (_container is not null)
            {
                await _container.StopAsync(_cancellationToken);
                await _container.DisposeAsync();
                await _dockerClient.Images.DeleteImageAsync(_container.Image.FullName, new ImageDeleteParameters { Force = true }, _cancellationToken);
            }

            if (_feedServer is not null)
            {
                await _feedServer.StopAsync(_cancellationToken);
                await _feedServer.DisposeAsync();
            }

            if (_network is not null)
            {
                await _network.DeleteAsync(_cancellationToken);
                await _network.DisposeAsync();
            }
        }

        _dockerClient?.Dispose();
        _cancellationTokenSource?.Dispose();
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Just a single test, not a perf issue")]
    public async Task AppRunningInDocker_ShouldBeHealthy()
    {
        // Arrange
        var containerImageTag = GenerateContainerImageTag();
        await BuildDockerImageAsync(containerImageTag, _cancellationToken);
        _container = await StartAppInContainerAsync(containerImageTag, _cancellationToken);
        var httpClient = new HttpClient { BaseAddress = GetAppBaseAddress(_container) };

        // Act
        var healthCheckResponse = await httpClient.GetAsync("healthz", _cancellationToken);
        var healthCheckToolResult = await _container.ExecAsync(["dotnet", "/app/mu88.HealthCheck.dll", "http://127.0.0.1:8080/podscrub/healthz"], _cancellationToken);

        // Assert
        await LogsShouldNotContainWarningsAsync(_container, _cancellationToken);
        await HealthCheckShouldBeHealthyAsync(healthCheckResponse, _cancellationToken);
        healthCheckToolResult.ExitCode.Should().Be(0);
    }

    [Test]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP014:Use a single instance of HttpClient", Justification = "Just a single test, not a perf issue")]
    public async Task AppRunningInDocker_ShouldProcessPodcastFeed()
    {
        // Arrange
        var testDataDir = Path.Combine(Path.GetTempPath(), $"podscrub-system-test-{Guid.NewGuid()}");
        const string feedServerAlias = "feed-server";

        try
        {
            var containerImageTag = GenerateContainerImageTag();
            await BuildDockerImageAsync(containerImageTag, _cancellationToken);

            _network = new NetworkBuilder().Build();
            await _network.CreateAsync(_cancellationToken);

            TestAudioGenerator.WriteTestFiles(testDataDir, $"http://{feedServerAlias}");

            _feedServer = new ContainerBuilder("nginx:alpine")
                .WithNetwork(_network)
                .WithNetworkAliases(feedServerAlias)
                .WithResourceMapping(new DirectoryInfo(testDataDir), "/usr/share/nginx/html")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPath("/feed.rss").ForPort(80)))
                .Build();
            await _feedServer.StartAsync(_cancellationToken);

            _container = BuildAppContainerWithFeed(_network, containerImageTag, feedServerAlias);
            await _container.StartAsync(_cancellationToken);

            var httpClient = new HttpClient { BaseAddress = GetAppBaseAddress(_container) };

            // Act — wait for PodScrub to poll and sync the feed (initial poll is immediate on startup)
            var feedResponse = await WaitForFeedSyncAsync(httpClient, "test-podcast", _cancellationToken);

            // Assert
            var healthCheckResponse = await httpClient.GetAsync("healthz", _cancellationToken);
            await HealthCheckShouldBeHealthyAsync(healthCheckResponse, _cancellationToken);

            feedResponse.Should().NotBeNull("feed should be available after sync");
            feedResponse.Should().Contain("<title>Test Podcast", "feed should contain the podcast title");
            feedResponse.Should().Contain("Test Episode 1", "feed should contain the episode");
            feedResponse.Should().Contain("/podscrub/audio/", "episode should have a PodScrub audio URL");

            // Check container logs for successful processing
            (string stdout, string stderr) = await _container.GetLogsAsync(ct: _cancellationToken);
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
        var buildResult = await Cli.Wrap("dotnet")
            .WithArguments([
                "publish",
                $"{apiProjectFile}",
                "--os",
                "linux",
                "--arch",
                "amd64",
                "/t:PublishContainersForMultipleFamilies",
                $"/p:ReleaseVersion={containerImageTag}",
                "/p:IsRelease=false",
                "/p:DoNotApplyGitHubScope=true",
            ])
            .ExecuteBufferedAsync(cancellationToken);
        buildResult.IsSuccess.Should().BeTrue();
        Console.WriteLine(buildResult.StandardOutput);
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
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Content root path: /app",
                    strategy => strategy.WithTimeout(TimeSpan.FromSeconds(30))))
            .Build();

    private static Uri GetAppBaseAddress(IContainer container)
        => new($"http://{container.Hostname}:{container.GetMappedPublicPort(8080)}/podscrub");

    private static async Task HealthCheckShouldBeHealthyAsync(HttpResponseMessage healthCheckResponse, CancellationToken cancellationToken)
    {
        healthCheckResponse.Should().Be200Ok();
        (await healthCheckResponse.Content.ReadAsStringAsync(cancellationToken)).Should().Be("Healthy");
    }

    private static async Task LogsShouldNotContainWarningsAsync(IContainer container, CancellationToken cancellationToken)
    {
        (string Stdout, string Stderr) logValues = await container.GetLogsAsync(ct: cancellationToken);
        Console.WriteLine($"Stderr:{Environment.NewLine}{logValues.Stderr}");
        Console.WriteLine($"Stdout:{Environment.NewLine}{logValues.Stdout}");
        logValues.Stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Should().NotContain(line => line.Contains("warn:", StringComparison.Ordinal));
    }

    [SuppressMessage("Design", "MA0076:Do not use implicit culture-sensitive ToString in interpolated strings", Justification = "Okay for me")]
    private static string GenerateContainerImageTag() => $"system-test-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
}
