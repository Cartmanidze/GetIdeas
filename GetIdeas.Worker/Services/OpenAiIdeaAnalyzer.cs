using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Models;
using GetIdeas.Worker.Options;

namespace GetIdeas.Worker.Services;

public sealed class OpenAiIdeaAnalyzer : IIdeaAnalyzer
{
    private const string SystemPrompt = """
                                        You are a venture analyst. Read a Reddit post and decide if it describes a concrete pain point
                                        that can be solved as a commercial product (SaaS, mobile app, service, AI workflow).
                                        Return only JSON with keys:
                                        - is_business_idea (boolean)
                                        - problem_summary (string, 1-2 concise sentences)
                                        - potential_solution (string, specific and concrete)
                                        - confidence_score (integer from 1 to 10)
                                        - reasoning (string, <= 30 words)
                                        """;

    private readonly HttpClient _httpClient;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiIdeaAnalyzer> _logger;

    public OpenAiIdeaAnalyzer(HttpClient httpClient, IOptions<LlmOptions> options, ILogger<OpenAiIdeaAnalyzer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IdeaAnalysis> AnalyzeAsync(RedditPost post, CancellationToken cancellationToken)
    {
        var prompt = BuildUserPrompt(post);
        var payload = new
        {
            model = _options.Model,
            temperature = 0.1,
            response_format = new
            {
                type = "json_object"
            },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = SystemPrompt
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"LLM request failed with status {(int)response.StatusCode}: {Truncate(responseBody, 500)}");
        }

        var assistantContent = ExtractAssistantContent(responseBody);
        var jsonPayload = ExtractJsonObject(assistantContent);
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            _logger.LogWarning("Model returned empty content for post {PostId}.", post.Id);
            return new IdeaAnalysis(false, "Model returned empty response.", string.Empty, 1, "invalid_output");
        }

        try
        {
            return ParseAnalysis(jsonPayload);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to parse model JSON for post {PostId}. Payload: {Payload}",
                post.Id,
                Truncate(jsonPayload, 600));
            return new IdeaAnalysis(false, "Could not parse model response.", string.Empty, 1, "parse_error");
        }
    }

    private static string BuildUserPrompt(RedditPost post)
    {
        return $"""
                Post metadata:
                - subreddit: {post.Subreddit}
                - author: {post.Author}
                - created_utc: {post.CreatedUtc:O}
                - permalink: {post.Permalink}

                Title:
                {post.Title}

                Body:
                {post.Body}
                """;
    }

    private static IdeaAnalysis ParseAnalysis(string jsonPayload)
    {
        using var document = JsonDocument.Parse(jsonPayload);
        var root = document.RootElement;

        var isBusinessIdea = ReadBoolean(root, "is_business_idea");
        var problemSummary = ReadString(root, "problem_summary");
        var potentialSolution = ReadString(root, "potential_solution");
        var confidence = Math.Clamp(ReadInt(root, "confidence_score", 1), 1, 10);
        var reasoning = ReadString(root, "reasoning");

        return new IdeaAnalysis(
            isBusinessIdea,
            problemSummary,
            potentialSolution,
            confidence,
            reasoning);
    }

    private static string ExtractAssistantContent(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("choices", out var choicesElement) ||
            choicesElement.ValueKind != JsonValueKind.Array ||
            choicesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("LLM response does not contain choices.");
        }

        var firstChoice = choicesElement[0];
        if (!firstChoice.TryGetProperty("message", out var messageElement))
        {
            throw new InvalidOperationException("LLM response does not contain a message.");
        }

        if (!messageElement.TryGetProperty("content", out var contentElement))
        {
            return string.Empty;
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString() ?? string.Empty;
        }

        if (contentElement.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var fragment in contentElement.EnumerateArray())
            {
                if (fragment.ValueKind == JsonValueKind.Object &&
                    fragment.TryGetProperty("text", out var textElement) &&
                    textElement.ValueKind == JsonValueKind.String)
                {
                    builder.Append(textElement.GetString());
                }
            }

            return builder.ToString();
        }

        return contentElement.GetRawText();
    }

    private static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                trimmed = trimmed[(firstLineEnd + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }

            trimmed = trimmed.Trim();
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return string.Empty;
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
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

    private static int ReadInt(JsonElement root, string propertyName, int defaultValue)
    {
        if (!root.TryGetProperty(propertyName, out var property))
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

    private static bool ReadBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var value) && value,
            JsonValueKind.Number => property.TryGetInt32(out var asInt) && asInt != 0,
            _ => false
        };
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
