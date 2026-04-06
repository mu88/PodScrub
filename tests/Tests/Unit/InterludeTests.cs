using NUnit.Framework;
using PodScrub.Domain;

namespace Tests.Unit;

[TestFixture]
[Category("Unit")]
public class InterludeTests
{
    [Test]
    public void Constructor_WithValidRange_CreatesInterlude()
    {
        // Arrange
        var start = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(20);

        // Act
        var interlude = new Interlude(start, end);

        // Assert
        interlude.Start.Should().Be(start);
        interlude.End.Should().Be(end);
        interlude.Duration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void Constructor_WithEndBeforeStart_ThrowsArgumentException()
    {
        // Arrange
        var start = TimeSpan.FromSeconds(20);
        var end = TimeSpan.FromSeconds(10);

        // Act
        var act = () => new Interlude(start, end);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("end");
    }

    [Test]
    public void Constructor_WithEqualStartAndEnd_ThrowsArgumentException()
    {
        // Arrange
        var start = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(10);

        // Act
        var act = () => new Interlude(start, end);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("end");
    }
}
