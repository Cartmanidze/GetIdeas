using Microsoft.Extensions.Options;
using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Models;
using GetIdeas.Worker.Options;

namespace GetIdeas.Worker.Services;

public sealed class PostPreFilter : IPostPreFilter
{
    private readonly ProcessingOptions _processingOptions;
    private readonly RedditOptions _redditOptions;

    public PostPreFilter(IOptions<ProcessingOptions> processingOptions, IOptions<RedditOptions> redditOptions)
    {
        _processingOptions = processingOptions.Value;
        _redditOptions = redditOptions.Value;
    }

    public bool ShouldAnalyze(RedditPost post, out string reason)
    {
        var text = post.CombinedText;
        if (string.IsNullOrWhiteSpace(text))
        {
            reason = "Post is empty.";
            return false;
        }

        var wordCount = CountWords(text);
        if (wordCount < _processingOptions.MinWordCount)
        {
            reason = $"Too short ({wordCount} words < {_processingOptions.MinWordCount}).";
            return false;
        }

        foreach (var stopPhrase in _processingOptions.StopPhrases.Where(static phrase => !string.IsNullOrWhiteSpace(phrase)))
        {
            if (text.Contains(stopPhrase, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Contains stop phrase: {stopPhrase}.";
                return false;
            }
        }

        if (_processingOptions.RequireKeywordMatch && _redditOptions.Keywords.Count > 0)
        {
            var containsKeyword = _redditOptions.Keywords
                .Where(static keyword => !string.IsNullOrWhiteSpace(keyword))
                .Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (!containsKeyword)
            {
                reason = "Does not match required keywords.";
                return false;
            }
        }

        reason = "Passed pre-filter.";
        return true;
    }

    private static int CountWords(string text)
    {
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }
}
