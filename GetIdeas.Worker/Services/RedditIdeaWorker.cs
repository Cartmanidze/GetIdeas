using Microsoft.Extensions.Options;
using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Models;
using GetIdeas.Worker.Options;

namespace GetIdeas.Worker.Services;

public sealed class RedditIdeaWorker : BackgroundService
{
    private readonly IRedditClient _redditClient;
    private readonly IPostPreFilter _preFilter;
    private readonly IIdeaAnalyzer _ideaAnalyzer;
    private readonly IPostRepository _repository;
    private readonly INotifier _notifier;
    private readonly ProcessingOptions _processingOptions;
    private readonly ILogger<RedditIdeaWorker> _logger;

    public RedditIdeaWorker(
        IRedditClient redditClient,
        IPostPreFilter preFilter,
        IIdeaAnalyzer ideaAnalyzer,
        IPostRepository repository,
        INotifier notifier,
        IOptions<ProcessingOptions> processingOptions,
        ILogger<RedditIdeaWorker> logger)
    {
        _redditClient = redditClient;
        _preFilter = preFilter;
        _ideaAnalyzer = ideaAnalyzer;
        _repository = repository;
        _notifier = notifier;
        _processingOptions = processingOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _repository.InitializeAsync(stoppingToken);
        var interval = TimeSpan.FromMinutes(_processingOptions.PollIntervalMinutes);

        _logger.LogInformation("Reddit idea worker started. Poll interval: {Interval}.", interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStartedAt = DateTimeOffset.UtcNow;
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected error in processing cycle.");
            }

            var elapsed = DateTimeOffset.UtcNow - cycleStartedAt;
            var delay = interval - elapsed;
            if (delay < TimeSpan.FromSeconds(5))
            {
                delay = TimeSpan.FromSeconds(5);
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var posts = await _redditClient.GetRecentPostsAsync(cancellationToken);
        var orderedPosts = posts.OrderByDescending(static post => post.CreatedUtc).ToList();

        var duplicates = 0;
        var skippedByPrefilter = 0;
        var analyzed = 0;
        var ideasFound = 0;
        var notificationsSent = 0;

        foreach (var post in orderedPosts)
        {
            if (await _repository.ExistsAsync(post.Id, cancellationToken))
            {
                duplicates++;
                continue;
            }

            if (!_preFilter.ShouldAnalyze(post, out var reason))
            {
                skippedByPrefilter++;
                await _repository.SaveAnalysisAsync(
                    post,
                    new IdeaAnalysis(false, reason, string.Empty, 1, "pre_filter_rejected"),
                    cancellationToken);
                continue;
            }

            IdeaAnalysis analysis;
            try
            {
                analysis = await _ideaAnalyzer.AnalyzeAsync(post, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "LLM analysis failed for post {PostId}.", post.Id);
                analysis = new IdeaAnalysis(false, "LLM request failed.", string.Empty, 1, "llm_error");
            }

            analyzed++;
            await _repository.SaveAnalysisAsync(post, analysis, cancellationToken);

            if (!analysis.IsBusinessIdea)
            {
                continue;
            }

            ideasFound++;
            if (analysis.ConfidenceScore < _processingOptions.MinimumConfidenceToNotify)
            {
                continue;
            }

            await _notifier.NotifyIdeaAsync(post, analysis, cancellationToken);
            notificationsSent++;
        }

        _logger.LogInformation(
            """
            Cycle complete.
            Total fetched: {TotalFetched}
            Duplicates skipped: {Duplicates}
            Prefilter skipped: {PrefilterSkipped}
            LLM analyzed: {Analyzed}
            Ideas found: {IdeasFound}
            Notifications sent: {NotificationsSent}
            """,
            orderedPosts.Count,
            duplicates,
            skippedByPrefilter,
            analyzed,
            ideasFound,
            notificationsSent);
    }
}
