using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Models;
using GetIdeas.Worker.Options;

namespace GetIdeas.Worker.Services;

public sealed class TelegramNotifier : INotifier
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyIdeaAsync(RedditPost post, IdeaAnalysis analysis, CancellationToken cancellationToken)
    {
        var endpoint = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";
        var payload = new
        {
            chat_id = _options.ChatId,
            text = BuildMessage(post, analysis),
            disable_web_page_preview = true
        };

        using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Telegram notification failed. Status={StatusCode}, Body={Body}",
            (int)response.StatusCode,
            Truncate(body, 400));
    }

    private static string BuildMessage(RedditPost post, IdeaAnalysis analysis)
    {
        var message = $"""
                       New business idea candidate

                       Subreddit: r/{post.Subreddit}
                       Title: {post.Title}
                       Confidence: {analysis.ConfidenceScore}/10

                       Problem:
                       {analysis.ProblemSummary}

                       Potential solution:
                       {analysis.PotentialSolution}

                       Link: {post.Permalink}
                       """;

        return Truncate(message, 3900);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
