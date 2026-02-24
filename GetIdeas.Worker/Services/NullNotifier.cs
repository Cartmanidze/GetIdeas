using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Models;

namespace GetIdeas.Worker.Services;

public sealed class NullNotifier : INotifier
{
    public Task NotifyIdeaAsync(RedditPost post, IdeaAnalysis analysis, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
