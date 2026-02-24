using GetIdeas.Worker.Models;

namespace GetIdeas.Worker.Interfaces;

public interface IPostPreFilter
{
    bool ShouldAnalyze(RedditPost post, out string reason);
}
