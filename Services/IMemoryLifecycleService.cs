using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Interface for memory lifecycle management operations.
/// Handles TTL expiration, access tracking, and archiving.
/// </summary>
public interface IMemoryLifecycleService
{
    /// <summary>
    /// Updates access tracking for a record (LastAccessedUnixMs and AccessCount).
    /// </summary>
    Task TrackAccessAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters out expired records from a list unless includeExpired is true.
    /// </summary>
    List<InternalMemoryRecord> FilterExpired(List<InternalMemoryRecord> records, bool includeExpired = false);

    /// <summary>
    /// Runs maintenance: archives expired and low-value memories.
    /// This should be called periodically (not on every request).
    /// </summary>
    Task RunMaintenanceAsync(CancellationToken cancellationToken = default);
}
