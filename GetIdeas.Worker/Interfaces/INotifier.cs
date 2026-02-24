using GetIdeas.Worker.Models;

namespace GetIdeas.Worker.Interfaces;

public interface INotifier
{
    Task NotifyIdeaAsync(RedditPost post, IdeaAnalysis analysis, CancellationToken cancellationToken);
}
