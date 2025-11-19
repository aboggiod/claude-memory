using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Interface for deduplication operations.
/// </summary>
public interface IDeduplicationService
{
    /// <summary>
    /// Finds potential duplicates for a new memory record.
    /// Returns the best match and its similarity score, or null if no duplicates found.
    /// </summary>
    Task<(InternalMemoryRecord? BestMatch, double Similarity)> FindDuplicateAsync(
        InternalMemoryRecord newRecord,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles deduplication when creating a new memory.
    /// Returns the final record (new or merged) that should be returned to the caller.
    /// </summary>
    Task<InternalMemoryRecord> HandleDeduplicationAsync(
        InternalMemoryRecord newRecord,
        CancellationToken cancellationToken = default);
}
