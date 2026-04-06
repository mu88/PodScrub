using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Tests.System;

/// <summary>
/// Generates synthetic audio files and RSS feed for system tests.
/// The key trick: the jingle clips in the episode are byte-for-byte identical
/// to the source jingle, guaranteeing a reliable fingerprint match.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class TestAudioGenerator
{
    private const int SampleRate = 44100;

    /// <summary>Duration of the jingle source file in seconds.</summary>
    public static TimeSpan JingleSourceInterludeStartBegin => TimeSpan.FromSeconds(5);

    public static TimeSpan JingleSourceInterludeStartEnd => TimeSpan.FromSeconds(10);

    public static TimeSpan JingleSourceInterludeEndBegin => TimeSpan.FromSeconds(13);

    public static TimeSpan JingleSourceInterludeEndEnd => TimeSpan.FromSeconds(18);

    /// <summary>Expected interlude location in the episode.</summary>
    public static TimeSpan ExpectedInterludeStart => TimeSpan.FromSeconds(10);

    public static TimeSpan ExpectedInterludeEnd => TimeSpan.FromSeconds(28);

    /// <summary>Total episode duration.</summary>
    public static double EpisodeDurationSeconds => 38.0;

    /// <summary>
    /// Writes all test files to the given directory:
    /// - jingle-source.wav (contains both jingle clips at known timestamps)
    /// - episode.wav (contains content + both jingle clips + interlude content + more content)
    /// - feed.rss (RSS feed referencing episode.wav)
    /// </summary>
    public static void WriteTestFiles(string directory, string feedServerBaseUrl)
    {
        Directory.CreateDirectory(directory);

        var jingleClip = GenerateChord(duration: 5.0);

        // jingle-source.wav: silence(5s) + jingle(5s) + silence(3s) + jingle(5s) + silence(2s) = 20s
        var jingleSource = Silence(5.0)
            .Concat(jingleClip)
            .Concat(Silence(3.0))
            .Concat(jingleClip)
            .Concat(Silence(2.0))
            .ToArray();
        WriteWav(Path.Combine(directory, "jingle-source.wav"), jingleSource);

        // episode.wav: content(10s) + jingle(5s) + interlude(8s) + jingle(5s) + content(10s) = 38s
        var episode = GenerateTone(300, 10.0)
            .Concat(jingleClip)
            .Concat(GenerateTone(150, 8.0))
            .Concat(jingleClip)
            .Concat(GenerateTone(350, 10.0))
            .ToArray();
        WriteWav(Path.Combine(directory, "episode.wav"), episode);

        var rssFeed = GenerateRssFeed(feedServerBaseUrl);
        File.WriteAllText(Path.Combine(directory, "feed.rss"), rssFeed, Encoding.UTF8);
    }

    private static double[] GenerateChord(double duration)
    {
        var sampleCount = (int)(SampleRate * duration);
        var samples = new double[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / SampleRate;
            samples[i] =
                (0.30 * Math.Sin(2 * Math.PI * 440.0 * t))
                + (0.25 * Math.Sin(2 * Math.PI * 554.37 * t))
                + (0.20 * Math.Sin(2 * Math.PI * 659.25 * t))
                + (0.15 * Math.Sin((2 * Math.PI * 880.0 * t) * (1 + (0.05 * Math.Sin(2 * Math.PI * 3 * t)))))
                + (0.10 * Math.Sin(2 * Math.PI * 1108.73 * t));
        }

        return samples;
    }

    private static double[] GenerateTone(double frequency, double duration)
    {
        var sampleCount = (int)(SampleRate * duration);
        var samples = new double[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / SampleRate;
            samples[i] = 0.3 * Math.Sin(2 * Math.PI * frequency * t);
        }

        return samples;
    }

    private static double[] Silence(double duration) => new double[(int)(SampleRate * duration)];

    private static void WriteWav(string path, double[] samples)
    {
        const short bitsPerSample = 16;
        const short channels = 1;

        var maxVal = samples.Max(Math.Abs);
        var scale = maxVal > 0 ? 30000.0 / maxVal : 1.0;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        var dataSize = samples.Length * (bitsPerSample / 8);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);

        foreach (var sample in samples)
        {
            writer.Write((short)Math.Clamp(sample * scale, short.MinValue, short.MaxValue));
        }
    }

    private static string GenerateRssFeed(string baseUrl) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">
          <channel>
            <title>Test Podcast</title>
            <description>A test podcast for system tests</description>
            <link>{baseUrl}</link>
            <item>
              <title>Test Episode 1</title>
              <enclosure url="{baseUrl}/episode.wav" length="3353644" type="audio/wav" />
              <guid>test-episode-1</guid>
              <pubDate>Mon, 01 Jan 2024 00:00:00 GMT</pubDate>
              <itunes:duration>38</itunes:duration>
            </item>
          </channel>
        </rss>
        """;
}
