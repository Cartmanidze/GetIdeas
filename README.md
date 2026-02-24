# GetIdeas

Background service on .NET 8 that scans selected subreddits, extracts potential business ideas with an LLM, stores results in SQLite, and optionally sends high-confidence ideas to Telegram.

## What it does

1. Authenticates in Reddit API via OAuth (`client_credentials`).
2. Pulls recent posts from configured subreddits.
3. Applies a cheap pre-filter (`min words`, `stop phrases`, optional keyword matching).
4. Sends candidate posts to an OpenAI-compatible LLM endpoint with strict JSON output requirements.
5. Saves all processed posts and analysis results into SQLite.
6. Sends Telegram notifications only for ideas with confidence above threshold.

## Configuration

Edit `GetIdeas.Worker/appsettings.json` or use environment variables:

- `Reddit__ClientId`
- `Reddit__ClientSecret`
- `Reddit__UserAgent`
- `Reddit__Subreddits__0`, `Reddit__Subreddits__1`, ...
- `Reddit__Keywords__0`, `Reddit__Keywords__1`, ...
- `Processing__PollIntervalMinutes`
- `Processing__MinWordCount`
- `Processing__RequireKeywordMatch`
- `Processing__MinimumConfidenceToNotify`
- `Llm__ApiKey`
- `Llm__Model`
- `Llm__Endpoint`
- `Storage__ConnectionString`
- `Telegram__Enabled`
- `Telegram__BotToken`
- `Telegram__ChatId`

`Reddit:UserAgent` should be specific, for example:

`windows:getideas:v1.0.0 (by /u/your_reddit_username)`

## Local run

```powershell
dotnet restore
dotnet run --project .\GetIdeas.Worker\GetIdeas.Worker.csproj
```

Database file is created automatically from `Storage:ConnectionString` (default: `getideas.db`).

## Docker

```powershell
docker compose up --build -d
```

By default, compose stores SQLite data in `./data/getideas.db`.
