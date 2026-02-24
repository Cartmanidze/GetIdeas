using GetIdeas.Worker.Models;

namespace GetIdeas.Worker.Interfaces;

public interface IIdeaAnalyzer
{
    Task<IdeaAnalysis> AnalyzeAsync(RedditPost post, CancellationToken cancellationToken);
}
