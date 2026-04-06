namespace PodScrub.Domain;

public class Interlude
{
    public Interlude(TimeSpan start, TimeSpan end)
    {
        if (end <= start)
        {
            throw new ArgumentException("Interlude end must be after start.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;
}
