using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Storage;

/// <summary>
/// Interface for memory storage operations. Implementations must handle CRUD operations
/// and ensure thread-safe access to the underlying storage mechanism.
/// </summary>
public interface IMemoryStore
{
    /// <summary>
    /// Add a new memory record to storage.
    /// </summary>
    Task<MemoryRecord> AddAsync(MemoryRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a memory record by ID.
    /// </summary>
    Task<MemoryRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve all memory records. For large datasets, consider using pagination.
    /// </summary>
    Task<List<MemoryRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing memory record.
    /// </summary>
    Task<MemoryRecord?> UpdateAsync(string id, MemoryRecord updatedRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a memory record by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete multiple memory records matching a predicate.
    /// </summary>
    Task DeleteManyAsync(Func<MemoryRecord, bool> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all memory records.
    /// </summary>
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
