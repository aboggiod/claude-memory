using ClaudeMemoryApi.Models;
using ClaudeMemoryApi.Storage;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Implementation of memory lifecycle management.
/// Handles TTL, access tracking, and archiving policies.
/// </summary>
public class MemoryLifecycleService : IMemoryLifecycleService
{
    private readonly IInternalMemoryStore _store;

    public MemoryLifecycleService(IInternalMemoryStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Updates access tracking for a record.
    /// </summary>
    public async Task TrackAccessAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetInternalByIdAsync(id, cancellationToken);
        if (record == null)
            return;

        var updated = record.WithAccessTracking();
        await _store.UpdateInternalAsync(id, updated, cancellationToken);
    }

    /// <summary>
    /// Filters out expired records unless includeExpired is true.
    /// Pinned records (Importance >= 5) are never filtered out.
    /// </summary>
    public List<InternalMemoryRecord> FilterExpired(List<InternalMemoryRecord> records, bool includeExpired = false)
    {
        if (includeExpired)
            return records;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return records.Where(r =>
        {
            // Pinned records are always included
            if (r.IsPinned)
                return true;

            // Check TTL expiration
            if (r.ExpiresAtUnixMs.HasValue && now > r.ExpiresAtUnixMs.Value)
                return false;

            return true;
        }).ToList();
    }

    /// <summary>
    /// Runs maintenance to archive old, expired, or low-value memories.
    ///
    /// Archiving policy:
    /// - Expired by TTL (unless pinned)
    /// - Very old (> 365 days), low importance (1-2), low access count (< 3)
    ///
    /// Pinned records (Importance >= 5) are never archived.
    /// </summary>
    public async Task RunMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        // Only run if the store supports archiving (WalMemoryStore)
        if (_store is not WalMemoryStore walStore)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oneYearAgoMs = now - (365L * 24 * 60 * 60 * 1000);

        await walStore.ArchiveAsync(record =>
        {
            // Never archive pinned records
            if (record.IsPinned)
                return false;

            // Archive if expired by TTL
            if (record.ExpiresAtUnixMs.HasValue && now > record.ExpiresAtUnixMs.Value)
                return true;

            // Archive if very old, low importance, and low access count
            if (record.Timestamp < oneYearAgoMs &&
                record.Importance <= 2 &&
                record.AccessCount < 3)
                return true;

            return false;
        }, cancellationToken);
    }
}
