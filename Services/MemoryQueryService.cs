using ClaudeMemoryApi.Models;
using ClaudeMemoryApi.Storage;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Implementation of IMemoryQueryService that filters and queries memories.
/// Preserves exact filtering logic from the original implementation.
/// </summary>
public class MemoryQueryService : IMemoryQueryService
{
    private readonly IMemoryStore _store;

    public MemoryQueryService(IMemoryStore store)
    {
        _store = store;
    }

    public async Task<List<MemoryRecord>> QueryAsync(
        long? sinceTimestamp = null,
        long? beforeTimestamp = null,
        string? role = null,
        string? tags = null,
        string? context = null,
        string? content = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.GetAllAsync(cancellationToken);

        var tagSet = string.IsNullOrWhiteSpace(tags)
            ? new HashSet<string>()
            : tags.Split(',').Select(t => t.Trim().ToLowerInvariant()).ToHashSet();

        var filtered = records.Where(r =>
        {
            if (sinceTimestamp.HasValue && r.Timestamp <= sinceTimestamp.Value) return false;
            if (beforeTimestamp.HasValue && r.Timestamp >= beforeTimestamp.Value) return false;
            if (!string.IsNullOrWhiteSpace(role) && r.Role != role.ToLowerInvariant()) return false;
            if (tagSet.Count > 0 && !r.Tags.Any(t => tagSet.Contains(t.ToLowerInvariant()))) return false;
            if (!string.IsNullOrWhiteSpace(context) &&
                (r.Context == null || !r.Context.Contains(context, StringComparison.OrdinalIgnoreCase))) return false;
            if (!string.IsNullOrWhiteSpace(content) &&
                !r.Content.Contains(content, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }).Take(limit).ToList();

        return filtered;
    }
}
