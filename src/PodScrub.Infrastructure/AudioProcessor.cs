using System.Diagnostics.CodeAnalysis;
using FFMpegCore;
using PodScrub.Domain;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public class AudioProcessor : IAudioProcessor
{
    public async Task ExtractClipAsync(string inputPath, TimeSpan start, TimeSpan end, string outputPath, CancellationToken cancellationToken)
    {
        await FFMpegArguments
            .FromFileInput(inputPath, verifyExists: true, options => options.Seek(start).WithDuration(end - start))
            .OutputToFile(outputPath, overwrite: true)
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();
    }

    public async Task<string> ConvertToWavAsync(string inputPath, string outputDirectory, CancellationToken cancellationToken)
    {
        var wavFileName = Path.ChangeExtension(Path.GetFileName(inputPath), ".wav");
        var outputPath = Path.Combine(outputDirectory, wavFileName);

        // FFmpeg cannot read and write to the same file
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + "_converted.wav");
        }

        await FFMpegArguments
            .FromFileInput(inputPath, verifyExists: true)
            .OutputToFile(outputPath, overwrite: true, options => options
                .WithCustomArgument("-ar 44100 -ac 1 -sample_fmt s16"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();

        return outputPath;
    }

    public async Task RemoveSegmentsAsync(string inputPath, IReadOnlyList<Interlude> segments, string outputPath, string? transitionTonePath, CancellationToken cancellationToken)
    {
        var orderedSegments = segments.OrderBy(segment => segment.Start).ToList();
        var tempFiles = new List<string>();
        var hasTransitionTone = !string.IsNullOrEmpty(transitionTonePath) && File.Exists(transitionTonePath);

        try
        {
            var previousEnd = TimeSpan.Zero;

            for (var index = 0; index < orderedSegments.Count; index++)
            {
                var segment = orderedSegments[index];
                if (segment.Start > previousEnd)
                {
                    var tempFile = Path.Combine(Path.GetDirectoryName(outputPath)!, $"temp_{index}.mp3");
                    tempFiles.Add(tempFile);

                    await FFMpegArguments
                        .FromFileInput(inputPath, verifyExists: true, options => options.Seek(previousEnd).WithDuration(segment.Start - previousEnd))
                        .OutputToFile(tempFile, overwrite: true)
                        .CancellableThrough(cancellationToken)
                        .ProcessAsynchronously();

                    if (hasTransitionTone)
                    {
                        tempFiles.Add(transitionTonePath!);
                    }
                }

                previousEnd = segment.End;
            }

            var probe = await FFProbe.AnalyseAsync(inputPath, cancellationToken: cancellationToken);
            if (probe.Duration > previousEnd)
            {
                var tempFile = Path.Combine(Path.GetDirectoryName(outputPath)!, $"temp_{orderedSegments.Count}.mp3");
                tempFiles.Add(tempFile);

                await FFMpegArguments
                    .FromFileInput(inputPath, verifyExists: true, options => options.Seek(previousEnd))
                    .OutputToFile(tempFile, overwrite: true)
                    .CancellableThrough(cancellationToken)
                    .ProcessAsynchronously();
            }

            // Remove trailing transition if the last item is the transition file
            if (hasTransitionTone && tempFiles.Count > 0 && string.Equals(tempFiles[^1], transitionTonePath, StringComparison.Ordinal))
            {
                tempFiles.RemoveAt(tempFiles.Count - 1);
            }

            await ConcatenateFilesAsync(tempFiles, outputPath, cancellationToken);
        }
        finally
        {
            foreach (var tempFile in tempFiles
                         .Where(file => !string.Equals(file, transitionTonePath, StringComparison.Ordinal))
                         .Where(File.Exists))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static async Task ConcatenateFilesAsync(List<string> inputFiles, string outputPath, CancellationToken cancellationToken)
    {
        if (inputFiles.Count == 0)
        {
            return;
        }

        if (inputFiles.Count == 1)
        {
            File.Copy(inputFiles[0], outputPath, overwrite: true);
            return;
        }

        var listFilePath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"concat_{Path.GetFileNameWithoutExtension(outputPath)}.txt");
        try
        {
            var lines = inputFiles.Select(file => $"file '{file.Replace("'", "'\\''", StringComparison.Ordinal)}'");
            await File.WriteAllLinesAsync(listFilePath, lines, cancellationToken);

            await FFMpegArguments
                .FromFileInput(listFilePath, verifyExists: true, options => options.WithCustomArgument("-f concat -safe 0"))
                .OutputToFile(outputPath, overwrite: true, options => options.WithCustomArgument("-c copy"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();
        }
        finally
        {
            if (File.Exists(listFilePath))
            {
                File.Delete(listFilePath);
            }
        }
    }
}
