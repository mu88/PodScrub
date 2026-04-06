namespace PodScrub.Domain;

public interface IAudioProcessor
{
    Task ExtractClipAsync(string inputPath, TimeSpan start, TimeSpan end, string outputPath, CancellationToken cancellationToken);

    Task<string> ConvertToWavAsync(string inputPath, string outputDirectory, CancellationToken cancellationToken);

    Task RemoveSegmentsAsync(string inputPath, IReadOnlyList<Interlude> segments, string outputPath, string? transitionTonePath, CancellationToken cancellationToken);
}
