using GetIdeas.Worker.Models;

namespace GetIdeas.Worker.Interfaces;

public interface IRedditClient
{
    Task<IReadOnlyCollection<RedditPost>> GetRecentPostsAsync(CancellationToken cancellationToken);
}
