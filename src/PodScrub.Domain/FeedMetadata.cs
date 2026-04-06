namespace PodScrub.Domain;

public class FeedMetadata
{
    public FeedMetadata(string title, string description, string? imageUrl, string link)
    {
        Title = title;
        Description = description;
        ImageUrl = imageUrl;
        Link = link;
    }

    public string Title { get; }

    public string Description { get; }

    public string? ImageUrl { get; }

    public string Link { get; }
}
