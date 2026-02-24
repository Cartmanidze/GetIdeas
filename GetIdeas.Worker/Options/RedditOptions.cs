namespace GetIdeas.Worker.Options;

public sealed class RedditOptions
{
    public const string SectionName = "Reddit";

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string UserAgent { get; init; } = string.Empty;

    public List<string> Subreddits { get; init; } = [];

    public List<string> Keywords { get; init; } = [];

    public int MaxPostsPerSubreddit { get; init; } = 75;

    public int LookbackHours { get; init; } = 24;
}
