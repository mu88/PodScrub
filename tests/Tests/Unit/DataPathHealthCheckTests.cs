using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using PodScrub.Api;
using PodScrub.Application;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class DataPathHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenDataPathIsWritable_ReturnsHealthy()
    {
        // Arrange
        var options = Options.Create(new PodScrubOptions { DataPath = "/data/podscrub" });
        var fileSystem = Substitute.For<IFileSystem>();
        var check = new DataPathHealthCheck(options, fileSystem);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("/data/podscrub");
    }

    [Test]
    public async Task CheckHealthAsync_WhenCreateDirectoryThrows_ReturnsUnhealthy()
    {
        // Arrange
        var options = Options.Create(new PodScrubOptions { DataPath = "/readonly/path" });
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.When(fs => fs.CreateDirectory(Arg.Any<string>()))
            .Do(_ => throw new UnauthorizedAccessException("permission denied"));
        var check = new DataPathHealthCheck(options, fileSystem);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("/readonly/path");
    }

    [Test]
    public async Task CheckHealthAsync_WhenWriteAllTextThrows_ReturnsUnhealthy()
    {
        // Arrange
        var options = Options.Create(new PodScrubOptions { DataPath = "/data/podscrub" });
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.When(fs => fs.WriteAllText(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => throw new IOException("disk full"));
        var check = new DataPathHealthCheck(options, fileSystem);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<IOException>();
    }
}
