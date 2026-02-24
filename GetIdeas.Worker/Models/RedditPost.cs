namespace GetIdeas.Worker.Models;

public sealed record RedditPost(
    string Id,
    string Subreddit,
    string Title,
    string Body,
    string Author,
    DateTimeOffset CreatedUtc,
    string Permalink,
    string Url)
{
    public string CombinedText => string.IsNullOrWhiteSpace(Body) ? Title : $"{Title}\n\n{Body}";
}
