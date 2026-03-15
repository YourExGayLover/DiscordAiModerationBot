using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Core.Interfaces;

public interface IDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<GuildSettings?> GetGuildSettingsAsync(long guildId, CancellationToken cancellationToken = default);
    Task UpsertGuildSettingsAsync(GuildSettings settings, CancellationToken cancellationToken = default);
    Task UpsertRuleAsync(RuleRecord rule, CancellationToken cancellationToken = default);
    Task<List<RuleRecord>> ListRulesAsync(long guildId, CancellationToken cancellationToken = default);
    Task<bool> RemoveRuleAsync(long guildId, string name, CancellationToken cancellationToken = default);
    Task<long> InsertAlertAsync(AlertRecord alert, CancellationToken cancellationToken = default);
    Task<bool> SetAlertFeedbackAsync(long alertId, long guildId, string status, string? notes, ulong reviewerUserId, CancellationToken cancellationToken = default);
    Task<List<AlertRecord>> ListAlertsAsync(long guildId, string status, int limit, CancellationToken cancellationToken = default);
    Task<List<FeedbackExample>> GetFeedbackExamplesAsync(long guildId, int limit, CancellationToken cancellationToken = default);
}
