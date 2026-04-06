namespace PodScrub.Domain;

public class Feed
{
    public Feed(string name, string url, IReadOnlyList<Jingle> jingles, int? processLatest = null)
    {
        Name = name;
        Url = url;
        Jingles = jingles;
        ProcessLatest = processLatest;
    }

    public string Name { get; }

    public string Url { get; }

    public IReadOnlyList<Jingle> Jingles { get; }

    public int? ProcessLatest { get; }
}
