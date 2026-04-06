namespace PodScrub.Domain;

public class Jingle
{
    public Jingle(JingleType type, string sourceEpisodeUrl, TimeSpan timestampStart, TimeSpan timestampEnd, string group = "default")
    {
        Type = type;
        SourceEpisodeUrl = sourceEpisodeUrl;
        TimestampStart = timestampStart;
        TimestampEnd = timestampEnd;
        Group = group;
    }

    public JingleType Type { get; }

    public string Group { get; }

    public string SourceEpisodeUrl { get; }

    public TimeSpan TimestampStart { get; }

    public TimeSpan TimestampEnd { get; }

    public string? AudioFilePath { get; private set; }

    public void SetAudioFilePath(string path) => AudioFilePath = path;
}
