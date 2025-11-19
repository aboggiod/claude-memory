using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Interface for memory querying and filtering operations.
/// </summary>
public interface IMemoryQueryService
{
    /// <summary>
    /// Query memories with filters and pagination.
    /// </summary>
    Task<List<MemoryRecord>> QueryAsync(
        long? sinceTimestamp = null,
        long? beforeTimestamp = null,
        string? role = null,
        string? tags = null,
        string? context = null,
        string? content = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
