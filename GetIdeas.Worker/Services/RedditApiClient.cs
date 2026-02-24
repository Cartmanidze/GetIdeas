using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Models;
using GetIdeas.Worker.Options;

namespace GetIdeas.Worker.Services;

public sealed class RedditApiClient : IRedditClient
{
    private static readonly Uri RedditTokenEndpoint = new("https://www.reddit.com/api/v1/access_token");
    private readonly HttpClient _httpClient;
    private readonly RedditOptions _options;
    private readonly ILogger<RedditApiClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc = DateTimeOffset.MinValue;

    public RedditApiClient(HttpClient httpClient, IOptions<RedditOptions> options, ILogger<RedditApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<RedditPost>> GetRecentPostsAsync(CancellationToken cancellationToken)
    {
        var sinceUtc = DateTimeOffset.UtcNow.AddHours(-Math.Abs(_options.LookbackHours));
        var results = new Dictionary<string, RedditPost>(StringComparer.OrdinalIgnoreCase);

        foreach (var subreddit in _options.Subreddits
                     .Where(static item => !string.IsNullOrWhiteSpace(item))
                     .Select(static item => item.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var posts = await GetPostsForSubredditAsync(subreddit, sinceUtc, cancellationToken);
            foreach (var post in posts)
            {
                results[post.Id] = post;
            }
        }

        _logger.LogInformation("Fetched {Count} Reddit posts from {SubredditCount} subreddit(s).", results.Count, _options.Subreddits.Count);
        return results.Values.ToArray();
    }

    private async Task<IReadOnlyCollection<RedditPost>> GetPostsForSubredditAsync(
        string subreddit,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(_options.MaxPostsPerSubreddit, 1, 100);
        var endpoint = $"https://oauth.reddit.com/r/{Uri.EscapeDataString(subreddit)}/new?limit={limit}";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = await EnsureAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Reddit returned 401 for /r/{Subreddit}. Refreshing token and retrying.", subreddit);
                InvalidateAccessToken();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to fetch /r/{Subreddit}. Status={StatusCode}. Body={Body}",
                    subreddit,
                    (int)response.StatusCode,
                    Truncate(body, 400));
                return Array.Empty<RedditPost>();
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
            return ParseRedditListing(document.RootElement, sinceUtc);
        }

        return Array.Empty<RedditPost>();
    }

    private IReadOnlyCollection<RedditPost> ParseRedditListing(JsonElement root, DateTimeOffset sinceUtc)
    {
        var posts = new List<RedditPost>();
        if (!root.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("children", out var childrenElement) ||
            childrenElement.ValueKind != JsonValueKind.Array)
        {
            return posts;
        }

        foreach (var child in childrenElement.EnumerateArray())
        {
            if (!child.TryGetProperty("data", out var postElement))
            {
                continue;
            }

            var id = GetString(postElement, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var createdAt = DateTimeOffset.FromUnixTimeSeconds(GetUnixSeconds(postElement, "created_utc"));
            if (createdAt < sinceUtc)
            {
                continue;
            }

            var permalink = GetString(postElement, "permalink");
            var absolutePermalink = string.IsNullOrWhiteSpace(permalink)
                ? string.Empty
                : $"https://www.reddit.com{permalink}";

            posts.Add(new RedditPost(
                id,
                GetString(postElement, "subreddit"),
                GetString(postElement, "title"),
                GetString(postElement, "selftext"),
                GetString(postElement, "author"),
                createdAt,
                absolutePermalink,
                GetString(postElement, "url")));
        }

        return posts;
    }

    private async Task<string> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, RedditTokenEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            request.Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            ]);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Reddit token request failed with status {(int)response.StatusCode}: {Truncate(responseContent, 500)}");
            }

            using var document = JsonDocument.Parse(responseContent);
            var token = GetString(document.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Reddit token response does not contain access_token.");
            }

            var expiresInSeconds = GetInt(document.RootElement, "expires_in", 3600);
            _accessToken = token;
            _accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds - 60);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateAccessToken()
    {
        _accessToken = null;
        _accessTokenExpiresAtUtc = DateTimeOffset.MinValue;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => string.Empty
        };
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return defaultValue;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return defaultValue;
    }

    private static long GetUnixSeconds(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            if (property.TryGetInt64(out var asInt64))
            {
                return asInt64;
            }

            if (property.TryGetDouble(out var asDouble))
            {
                return Convert.ToInt64(Math.Floor(asDouble), CultureInfo.InvariantCulture);
            }
        }

        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var asParsed))
        {
            return Convert.ToInt64(Math.Floor(asParsed), CultureInfo.InvariantCulture);
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
