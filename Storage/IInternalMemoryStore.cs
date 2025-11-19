using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Storage;

/// <summary>
/// Internal storage interface that works with InternalMemoryRecord.
/// This is used internally by the system for advanced features while
/// the public IMemoryStore continues to work with MemoryRecord for backward compatibility.
/// </summary>
public interface IInternalMemoryStore
{
    /// <summary>
    /// Add a new internal memory record to storage.
    /// </summary>
    Task<InternalMemoryRecord> AddInternalAsync(InternalMemoryRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve an internal memory record by ID.
    /// </summary>
    Task<InternalMemoryRecord?> GetInternalByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve all internal memory records.
    /// </summary>
    Task<List<InternalMemoryRecord>> GetAllInternalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing internal memory record.
    /// </summary>
    Task<InternalMemoryRecord?> UpdateInternalAsync(string id, InternalMemoryRecord updatedRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete multiple internal memory records matching a predicate.
    /// </summary>
    Task DeleteManyInternalAsync(Func<InternalMemoryRecord, bool> predicate, CancellationToken cancellationToken = default);
}
