using ClaudeMemoryApi.Models;
using ClaudeMemoryApi.Storage;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Implementation of IMemoryQueryService that filters and queries memories.
/// Preserves exact filtering logic from the original implementation.
/// Phase 5 enhancement: Adds lifecycle filtering and optional access tracking.
/// </summary>
public class MemoryQueryService : IMemoryQueryService
{
    private readonly IMemoryStore _store;
    private readonly IInternalMemoryStore? _internalStore;
    private readonly IMemoryLifecycleService? _lifecycleService;

    public MemoryQueryService(
        IMemoryStore store,
        IInternalMemoryStore? internalStore = null,
        IMemoryLifecycleService? lifecycleService = null)
    {
        _store = store;
        _internalStore = internalStore;
        _lifecycleService = lifecycleService;
    }

    public async Task<List<MemoryRecord>> QueryAsync(
        long? sinceTimestamp = null,
        long? beforeTimestamp = null,
        string? role = null,
        string? tags = null,
        string? context = null,
        string? content = null,
        int limit = 100,
        bool includeExpired = false,
        CancellationToken cancellationToken = default)
    {
        // Get internal records if available for lifecycle filtering
        List<InternalMemoryRecord> internalRecords;
        if (_internalStore != null)
        {
            internalRecords = await _internalStore.GetAllInternalAsync(cancellationToken);

            // Apply lifecycle filtering (expired records)
            if (_lifecycleService != null)
            {
                internalRecords = _lifecycleService.FilterExpired(internalRecords, includeExpired);
            }
        }
        else
        {
            // Fallback to regular store
            var records = await _store.GetAllAsync(cancellationToken);
            internalRecords = records.Select(InternalMemoryRecord.FromMemoryRecord).ToList();
        }

        var tagSet = string.IsNullOrWhiteSpace(tags)
            ? new HashSet<string>()
            : tags.Split(',').Select(t => t.Trim().ToLowerInvariant()).ToHashSet();

        var filtered = internalRecords.Where(r =>
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

        // Track access for returned records (fire-and-forget for performance)
        if (_lifecycleService != null)
        {
            _ = Task.Run(async () =>
            {
                foreach (var record in filtered)
                {
                    try
                    {
                        await _lifecycleService.TrackAccessAsync(record.Id);
                    }
                    catch
                    {
                        // Ignore access tracking errors
                    }
                }
            });
        }

        // Convert to external MemoryRecord format
        return filtered.Select(r => r.ToMemoryRecord()).ToList();
    }
}

