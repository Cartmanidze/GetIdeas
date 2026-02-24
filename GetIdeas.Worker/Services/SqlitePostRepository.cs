using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using GetIdeas.Worker.Interfaces;
using GetIdeas.Worker.Models;
using GetIdeas.Worker.Options;

namespace GetIdeas.Worker.Services;

public sealed class SqlitePostRepository : IPostRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _isInitialized;

    public SqlitePostRepository(IOptions<StorageOptions> storageOptions)
    {
        _connectionString = storageOptions.Value.ConnectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            EnsureDatabaseDirectory(_connectionString);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS analyzed_posts (
                                      reddit_post_id TEXT PRIMARY KEY,
                                      subreddit TEXT NOT NULL,
                                      title TEXT NOT NULL,
                                      body TEXT NOT NULL,
                                      author TEXT NOT NULL,
                                      url TEXT NOT NULL,
                                      permalink TEXT NOT NULL,
                                      created_utc INTEGER NOT NULL,
                                      processed_utc INTEGER NOT NULL,
                                      is_business_idea INTEGER NOT NULL,
                                      confidence_score INTEGER NOT NULL,
                                      problem_summary TEXT NOT NULL,
                                      potential_solution TEXT NOT NULL,
                                      reasoning TEXT NOT NULL
                                  );

                                  CREATE INDEX IF NOT EXISTS idx_analyzed_posts_processed_utc ON analyzed_posts(processed_utc);
                                  CREATE INDEX IF NOT EXISTS idx_analyzed_posts_business_idea ON analyzed_posts(is_business_idea, confidence_score);
                                  """;

            await command.ExecuteNonQueryAsync(cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<bool> ExistsAsync(string redditPostId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM analyzed_posts WHERE reddit_post_id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", redditPostId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    public async Task SaveAnalysisAsync(RedditPost post, IdeaAnalysis analysis, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO analyzed_posts (
                                  reddit_post_id,
                                  subreddit,
                                  title,
                                  body,
                                  author,
                                  url,
                                  permalink,
                                  created_utc,
                                  processed_utc,
                                  is_business_idea,
                                  confidence_score,
                                  problem_summary,
                                  potential_solution,
                                  reasoning
                              )
                              VALUES (
                                  $reddit_post_id,
                                  $subreddit,
                                  $title,
                                  $body,
                                  $author,
                                  $url,
                                  $permalink,
                                  $created_utc,
                                  $processed_utc,
                                  $is_business_idea,
                                  $confidence_score,
                                  $problem_summary,
                                  $potential_solution,
                                  $reasoning
                              )
                              ON CONFLICT(reddit_post_id) DO UPDATE SET
                                  subreddit = excluded.subreddit,
                                  title = excluded.title,
                                  body = excluded.body,
                                  author = excluded.author,
                                  url = excluded.url,
                                  permalink = excluded.permalink,
                                  created_utc = excluded.created_utc,
                                  processed_utc = excluded.processed_utc,
                                  is_business_idea = excluded.is_business_idea,
                                  confidence_score = excluded.confidence_score,
                                  problem_summary = excluded.problem_summary,
                                  potential_solution = excluded.potential_solution,
                                  reasoning = excluded.reasoning;
                              """;

        command.Parameters.AddWithValue("$reddit_post_id", post.Id);
        command.Parameters.AddWithValue("$subreddit", post.Subreddit);
        command.Parameters.AddWithValue("$title", post.Title);
        command.Parameters.AddWithValue("$body", post.Body);
        command.Parameters.AddWithValue("$author", post.Author);
        command.Parameters.AddWithValue("$url", post.Url);
        command.Parameters.AddWithValue("$permalink", post.Permalink);
        command.Parameters.AddWithValue("$created_utc", post.CreatedUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$processed_utc", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$is_business_idea", analysis.IsBusinessIdea ? 1 : 0);
        command.Parameters.AddWithValue("$confidence_score", analysis.ConfidenceScore);
        command.Parameters.AddWithValue("$problem_summary", analysis.ProblemSummary);
        command.Parameters.AddWithValue("$potential_solution", analysis.PotentialSolution);
        command.Parameters.AddWithValue("$reasoning", analysis.Reasoning);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureDatabaseDirectory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fullPath = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
