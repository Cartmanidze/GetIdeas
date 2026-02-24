using GetIdeas.Worker.Models;

namespace GetIdeas.Worker.Interfaces;

public interface IPostRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string redditPostId, CancellationToken cancellationToken);

    Task SaveAnalysisAsync(RedditPost post, IdeaAnalysis analysis, CancellationToken cancellationToken);
}
