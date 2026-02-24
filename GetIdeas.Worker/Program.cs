using Microsoft.Extensions.Options;
using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Options;
using GetIdeas.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<RedditOptions>()
    .Bind(builder.Configuration.GetSection(RedditOptions.SectionName))
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ClientId), "Reddit:ClientId is required.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ClientSecret), "Reddit:ClientSecret is required.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.UserAgent), "Reddit:UserAgent is required.")
    .Validate(static options => options.Subreddits.Count > 0, "At least one subreddit must be configured.")
    .ValidateOnStart();

builder.Services
    .AddOptions<ProcessingOptions>()
    .Bind(builder.Configuration.GetSection(ProcessingOptions.SectionName))
    .Validate(static options => options.PollIntervalMinutes >= 1, "Processing:PollIntervalMinutes must be >= 1.")
    .Validate(static options => options.MinWordCount >= 1, "Processing:MinWordCount must be >= 1.")
    .Validate(static options => options.MinimumConfidenceToNotify is >= 1 and <= 10, "Processing:MinimumConfidenceToNotify must be in range 1..10.")
    .ValidateOnStart();

builder.Services
    .AddOptions<LlmOptions>()
    .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ApiKey), "Llm:ApiKey is required.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.Model), "Llm:Model is required.")
    .Validate(static options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _), "Llm:Endpoint must be a valid absolute URL.")
    .Validate(static options => options.TimeoutSeconds is >= 10 and <= 180, "Llm:TimeoutSeconds must be in range 10..180.")
    .ValidateOnStart();

builder.Services
    .AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Storage:ConnectionString is required.")
    .ValidateOnStart();

builder.Services
    .AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection(TelegramOptions.SectionName))
    .Validate(
        static options =>
            !options.Enabled || (!string.IsNullOrWhiteSpace(options.BotToken) && !string.IsNullOrWhiteSpace(options.ChatId)),
        "Telegram:BotToken and Telegram:ChatId are required when Telegram:Enabled=true.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IRedditClient, RedditApiClient>();
builder.Services.AddHttpClient<IIdeaAnalyzer, OpenAiIdeaAnalyzer>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddHttpClient<TelegramNotifier>();

builder.Services.AddSingleton<IPostPreFilter, PostPreFilter>();
builder.Services.AddSingleton<IPostRepository, SqlitePostRepository>();
builder.Services.AddSingleton<INotifier>(serviceProvider =>
{
    var telegramOptions = serviceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return telegramOptions.Enabled
        ? serviceProvider.GetRequiredService<TelegramNotifier>()
        : new NullNotifier();
});

builder.Services.AddHostedService<RedditIdeaWorker>();

var host = builder.Build();
host.Run();
