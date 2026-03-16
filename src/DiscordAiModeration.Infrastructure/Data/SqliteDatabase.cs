using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using DiscordAiModeration.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Infrastructure.Data;

public sealed class SqliteDatabase : IDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(IOptions<StorageOptions> storageOptions)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = storageOptions.Value.SqlitePath
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = """
                  CREATE TABLE IF NOT EXISTS GuildSettings (
                      GuildId INTEGER PRIMARY KEY,
                      AlertChannelId INTEGER NULL,
                      PingRoleId INTEGER NULL,
                      ConfidenceThreshold INTEGER NOT NULL DEFAULT 70,
                      AiEnabled INTEGER NOT NULL DEFAULT 1
                  );

                  CREATE TABLE IF NOT EXISTS Rules (
                      Id INTEGER PRIMARY KEY AUTOINCREMENT,
                      GuildId INTEGER NOT NULL,
                      Name TEXT NOT NULL,
                      Description TEXT NOT NULL,
                      ExamplesJson TEXT NULL,
                      UNIQUE(GuildId, Name)
                  );

                  CREATE TABLE IF NOT EXISTS Alerts (
                      Id INTEGER PRIMARY KEY AUTOINCREMENT,
                      GuildId INTEGER NOT NULL,
                      MessageId INTEGER NOT NULL,
                      ChannelId INTEGER NOT NULL,
                      UserId INTEGER NOT NULL,
                      RuleName TEXT NOT NULL,
                      Confidence INTEGER NOT NULL,
                      Reason TEXT NULL,
                      MessageContent TEXT NOT NULL,
                      FeedbackStatus TEXT NOT NULL DEFAULT 'pending',
                      FeedbackNotes TEXT NULL,
                      ReviewedByUserId INTEGER NULL,
                      CreatedUtc TEXT NOT NULL
                  );
                  """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<GuildSettings?> GetGuildSettingsAsync(long guildId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GuildId, AlertChannelId, PingRoleId, ConfidenceThreshold, AiEnabled FROM GuildSettings WHERE GuildId=$guildId";
        command.Parameters.AddWithValue("$guildId", guildId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new GuildSettings
        {
            GuildId = reader.GetInt64(0),
            AlertChannelId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
            PingRoleId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
            ConfidenceThreshold = reader.GetInt32(3),
            AiEnabled = reader.GetInt64(4) == 1
        };
    }

    public async Task UpsertGuildSettingsAsync(GuildSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO GuildSettings (GuildId, AlertChannelId, PingRoleId, ConfidenceThreshold, AiEnabled)
                              VALUES ($guildId, $alertChannelId, $pingRoleId, $threshold, $enabled)
                              ON CONFLICT(GuildId) DO UPDATE SET
                                  AlertChannelId = excluded.AlertChannelId,
                                  PingRoleId = excluded.PingRoleId,
                                  ConfidenceThreshold = excluded.ConfidenceThreshold,
                                  AiEnabled = excluded.AiEnabled;
                              """;

        command.Parameters.AddWithValue("$guildId", settings.GuildId);
        command.Parameters.AddWithValue("$alertChannelId", (object?)settings.AlertChannelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$pingRoleId", (object?)settings.PingRoleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$threshold", settings.ConfidenceThreshold);
        command.Parameters.AddWithValue("$enabled", settings.AiEnabled ? 1 : 0);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertRuleAsync(RuleRecord rule, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO Rules (GuildId, Name, Description, ExamplesJson)
                              VALUES ($guildId, $name, $description, $examplesJson)
                              ON CONFLICT(GuildId, Name) DO UPDATE SET
                                  Description = excluded.Description,
                                  ExamplesJson = excluded.ExamplesJson;
                              """;

        command.Parameters.AddWithValue("$guildId", rule.GuildId);
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$description", rule.Description);
        command.Parameters.AddWithValue("$examplesJson", (object?)rule.ExamplesJson ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<RuleRecord>> ListRulesAsync(long guildId, CancellationToken cancellationToken = default)
    {
        var rules = new List<RuleRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, GuildId, Name, Description, ExamplesJson FROM Rules WHERE GuildId=$guildId ORDER BY Name";
        command.Parameters.AddWithValue("$guildId", guildId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(new RuleRecord
            {
                Id = reader.GetInt64(0),
                GuildId = reader.GetInt64(1),
                Name = reader.GetString(2),
                Description = reader.GetString(3),
                ExamplesJson = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return rules;
    }

    public async Task<bool> RemoveRuleAsync(long guildId, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Rules WHERE GuildId=$guildId AND Name=$name";
        command.Parameters.AddWithValue("$guildId", guildId);
        command.Parameters.AddWithValue("$name", name);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> RemoveAllRulesAsync(long guildId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Rules WHERE GuildId=$guildId";
        command.Parameters.AddWithValue("$guildId", guildId);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> InsertAlertAsync(AlertRecord alert, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO Alerts (GuildId, MessageId, ChannelId, UserId, RuleName, Confidence, Reason, MessageContent, FeedbackStatus, CreatedUtc)
                              VALUES ($guildId, $messageId, $channelId, $userId, $ruleName, $confidence, $reason, $messageContent, $feedbackStatus, $createdUtc);
                              SELECT last_insert_rowid();
                              """;

        command.Parameters.AddWithValue("$guildId", alert.GuildId);
        command.Parameters.AddWithValue("$messageId", alert.MessageId);
        command.Parameters.AddWithValue("$channelId", alert.ChannelId);
        command.Parameters.AddWithValue("$userId", alert.UserId);
        command.Parameters.AddWithValue("$ruleName", alert.RuleName);
        command.Parameters.AddWithValue("$confidence", alert.Confidence);
        command.Parameters.AddWithValue("$reason", (object?)alert.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$messageContent", alert.MessageContent);
        command.Parameters.AddWithValue("$feedbackStatus", alert.FeedbackStatus);
        command.Parameters.AddWithValue("$createdUtc", alert.CreatedUtc.ToString("O"));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task<bool> SetAlertFeedbackAsync(long alertId, long guildId, string status, string? notes, ulong reviewerUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE Alerts
                              SET FeedbackStatus=$status,
                                  FeedbackNotes=$notes,
                                  ReviewedByUserId=$reviewedByUserId
                              WHERE Id=$alertId AND GuildId=$guildId;
                              """;

        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$reviewedByUserId", (long)reviewerUserId);
        command.Parameters.AddWithValue("$alertId", alertId);
        command.Parameters.AddWithValue("$guildId", guildId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<List<AlertRecord>> ListAlertsAsync(long guildId, string status, int limit, CancellationToken cancellationToken = default)
    {
        var alerts = new List<AlertRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = status.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? "SELECT Id, GuildId, MessageId, ChannelId, UserId, RuleName, Confidence, Reason, MessageContent, FeedbackStatus, FeedbackNotes, ReviewedByUserId, CreatedUtc FROM Alerts WHERE GuildId=$guildId ORDER BY Id DESC LIMIT $limit"
            : "SELECT Id, GuildId, MessageId, ChannelId, UserId, RuleName, Confidence, Reason, MessageContent, FeedbackStatus, FeedbackNotes, ReviewedByUserId, CreatedUtc FROM Alerts WHERE GuildId=$guildId AND FeedbackStatus=$status ORDER BY Id DESC LIMIT $limit";

        command.Parameters.AddWithValue("$guildId", guildId);
        command.Parameters.AddWithValue("$limit", limit);
        if (!status.Equals("all", StringComparison.OrdinalIgnoreCase))
            command.Parameters.AddWithValue("$status", status);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            alerts.Add(new AlertRecord
            {
                Id = reader.GetInt64(0),
                GuildId = reader.GetInt64(1),
                MessageId = reader.GetInt64(2),
                ChannelId = reader.GetInt64(3),
                UserId = reader.GetInt64(4),
                RuleName = reader.GetString(5),
                Confidence = reader.GetInt32(6),
                Reason = reader.IsDBNull(7) ? null : reader.GetString(7),
                MessageContent = reader.GetString(8),
                FeedbackStatus = reader.GetString(9),
                FeedbackNotes = reader.IsDBNull(10) ? null : reader.GetString(10),
                ReviewedByUserId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                CreatedUtc = DateTime.Parse(reader.GetString(12)).ToUniversalTime()
            });
        }

        return alerts;
    }

    public async Task<List<FeedbackExample>> GetFeedbackExamplesAsync(long guildId, int limit, CancellationToken cancellationToken = default)
    {
        var examples = new List<FeedbackExample>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT RuleName, MessageContent, FeedbackStatus, FeedbackNotes
                              FROM Alerts
                              WHERE GuildId=$guildId AND FeedbackStatus IN ('approved', 'rejected')
                              ORDER BY Id DESC
                              LIMIT $limit;
                              """;

        command.Parameters.AddWithValue("$guildId", guildId);
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            examples.Add(new FeedbackExample
            {
                RuleName = reader.GetString(0),
                MessageContent = reader.GetString(1),
                Outcome = reader.GetString(2),
                Notes = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return examples;
    }
}
