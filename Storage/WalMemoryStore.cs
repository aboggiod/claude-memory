using ClaudeMemoryApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeMemoryApi.Storage;

/// <summary>
/// Operation types for the write-ahead log.
/// </summary>
public enum WalOperationType
{
    Add,
    Update,
    Delete
}

/// <summary>
/// Represents a single operation in the write-ahead log.
/// </summary>
public record WalOperation(
    [property: JsonPropertyName("op")] WalOperationType Op,
    [property: JsonPropertyName("record")] InternalMemoryRecord? Record,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("timestamp")] long Timestamp
);

/// <summary>
/// Advanced storage implementation with write-ahead logging (WAL), compaction, and archiving.
///
/// Features:
/// - Write-ahead log for crash safety
/// - Automatic compaction based on configurable thresholds
/// - Archive support for expired/low-importance memories
/// - In-memory index for fast lookups
/// - Atomic operations with SemaphoreSlim
///
/// File structure:
/// - main.jsonl: Primary storage file
/// - wal.log: Write-ahead log of operations since last compaction
/// - archive.jsonl: Archived memories
/// </summary>
public class WalMemoryStore : IMemoryStore, IInternalMemoryStore
{
    private readonly string _dataDirectory;
    private readonly string _mainFilePath;
    private readonly string _walFilePath;
    private readonly string _archiveFilePath;
    private readonly SemaphoreSlim _semaphore;
    private readonly int _walMaxOperations;
    private readonly long _walMaxBytes;

    // In-memory index for fast lookups
    private Dictionary<string, InternalMemoryRecord> _memoryIndex;
    private int _walOperationCount;
    private bool _isInitialized;

    public WalMemoryStore(
        string dataDirectory,
        int walMaxOperations = 1000,
        long walMaxBytes = 5_000_000)
    {
        _dataDirectory = dataDirectory;
        Directory.CreateDirectory(_dataDirectory);

        _mainFilePath = Path.Combine(_dataDirectory, "main.jsonl");
        _walFilePath = Path.Combine(_dataDirectory, "wal.log");
        _archiveFilePath = Path.Combine(_dataDirectory, "archive.jsonl");

        _semaphore = new SemaphoreSlim(1, 1);
        _walMaxOperations = walMaxOperations;
        _walMaxBytes = walMaxBytes;

        _memoryIndex = new Dictionary<string, InternalMemoryRecord>(StringComparer.OrdinalIgnoreCase);
        _walOperationCount = 0;
        _isInitialized = false;
    }

    /// <summary>
    /// Initializes the store by replaying WAL and building the in-memory index.
    /// This is called lazily on first operation.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return; // Double-check after acquiring lock

            // Load main file
            if (File.Exists(_mainFilePath))
            {
                await foreach (var line in File.ReadLinesAsync(_mainFilePath, cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var record = JsonSerializer.Deserialize<InternalMemoryRecord>(line);
                        if (record != null)
                        {
                            _memoryIndex[record.Id] = record;
                        }
                    }
                    catch
                    {
                        // Skip corrupted lines
                    }
                }
            }

            // Replay WAL if it exists
            if (File.Exists(_walFilePath))
            {
                await ReplayWalAsync(cancellationToken);
                // After replaying WAL, compact to consolidate state
                await CompactAsync(cancellationToken);
            }

            _isInitialized = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Replays the write-ahead log to restore operations since last compaction.
    /// </summary>
    private async Task ReplayWalAsync(CancellationToken cancellationToken = default)
    {
        var operations = new List<WalOperation>();

        await foreach (var line in File.ReadLinesAsync(_walFilePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var op = JsonSerializer.Deserialize<WalOperation>(line);
                if (op != null) operations.Add(op);
            }
            catch
            {
                // Skip corrupted WAL entries
            }
        }

        // Apply operations in order
        foreach (var op in operations.OrderBy(o => o.Timestamp))
        {
            switch (op.Op)
            {
                case WalOperationType.Add:
                case WalOperationType.Update:
                    if (op.Record != null)
                    {
                        _memoryIndex[op.Record.Id] = op.Record;
                    }
                    break;

                case WalOperationType.Delete:
                    if (op.Id != null)
                    {
                        _memoryIndex.Remove(op.Id);
                    }
                    break;
            }
        }

        _walOperationCount = operations.Count;
    }

    /// <summary>
    /// Appends an operation to the write-ahead log.
    /// </summary>
    private async Task AppendToWalAsync(WalOperation operation, CancellationToken cancellationToken = default)
    {
        var line = JsonSerializer.Serialize(operation) + "\n";
        await File.AppendAllTextAsync(_walFilePath, line, cancellationToken);
        _walOperationCount++;

        // Check if compaction is needed
        var walInfo = new FileInfo(_walFilePath);
        if (_walOperationCount >= _walMaxOperations || walInfo.Length >= _walMaxBytes)
        {
            await CompactAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Compacts the storage by writing the current in-memory state to main.jsonl
    /// and truncating the WAL.
    /// </summary>
    private async Task CompactAsync(CancellationToken cancellationToken = default)
    {
        var tempPath = _mainFilePath + ".tmp";

        // Write all current records to temp file
        await using (var writer = new StreamWriter(tempPath, false))
        {
            foreach (var record in _memoryIndex.Values)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(record));
            }
        }

        // Atomically replace main file
        if (File.Exists(_mainFilePath))
        {
            File.Replace(tempPath, _mainFilePath, null);
        }
        else
        {
            File.Move(tempPath, _mainFilePath);
        }

        // Truncate WAL
        if (File.Exists(_walFilePath))
        {
            File.Delete(_walFilePath);
        }

        _walOperationCount = 0;
    }

    // ============================================================================
    // IMemoryStore Implementation (backward-compatible external API)
    // ============================================================================

    public async Task<MemoryRecord> AddAsync(MemoryRecord record, CancellationToken cancellationToken = default)
    {
        var internalRecord = InternalMemoryRecord.FromMemoryRecord(record);
        await AddInternalAsync(internalRecord, cancellationToken);
        return record;
    }

    public async Task<MemoryRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var internalRecord = await GetInternalByIdAsync(id, cancellationToken);
        return internalRecord?.ToMemoryRecord();
    }

    public async Task<List<MemoryRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var internalRecords = await GetAllInternalAsync(cancellationToken);
        return internalRecords.Select(r => r.ToMemoryRecord()).ToList();
    }

    public async Task<MemoryRecord?> UpdateAsync(string id, MemoryRecord updatedRecord, CancellationToken cancellationToken = default)
    {
        var existingInternal = await GetInternalByIdAsync(id, cancellationToken);
        if (existingInternal == null)
            return null;

        var updatedInternal = existingInternal with
        {
            Role = updatedRecord.Role,
            Content = updatedRecord.Content,
            Tags = updatedRecord.Tags,
            Context = updatedRecord.Context
        };

        await UpdateInternalAsync(id, updatedInternal, cancellationToken);
        return updatedRecord;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_memoryIndex.ContainsKey(id))
                return false;

            _memoryIndex.Remove(id);

            var operation = new WalOperation(
                Op: WalOperationType.Delete,
                Record: null,
                Id: id,
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            await AppendToWalAsync(operation, cancellationToken);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteManyAsync(Func<MemoryRecord, bool> predicate, CancellationToken cancellationToken = default)
    {
        await DeleteManyInternalAsync(r => predicate(r.ToMemoryRecord()), cancellationToken);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _memoryIndex.Clear();

            // Delete all files
            if (File.Exists(_mainFilePath)) File.Delete(_mainFilePath);
            if (File.Exists(_walFilePath)) File.Delete(_walFilePath);

            _walOperationCount = 0;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // ============================================================================
    // IInternalMemoryStore Implementation (internal API with extended features)
    // ============================================================================

    public async Task<InternalMemoryRecord> AddInternalAsync(InternalMemoryRecord record, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _memoryIndex[record.Id] = record;

            var operation = new WalOperation(
                Op: WalOperationType.Add,
                Record: record,
                Id: null,
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            await AppendToWalAsync(operation, cancellationToken);
            return record;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<InternalMemoryRecord?> GetInternalByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return _memoryIndex.TryGetValue(id, out var record) ? record : null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<InternalMemoryRecord>> GetAllInternalAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return _memoryIndex.Values.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<InternalMemoryRecord?> UpdateInternalAsync(string id, InternalMemoryRecord updatedRecord, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_memoryIndex.ContainsKey(id))
                return null;

            _memoryIndex[id] = updatedRecord;

            var operation = new WalOperation(
                Op: WalOperationType.Update,
                Record: updatedRecord,
                Id: null,
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            await AppendToWalAsync(operation, cancellationToken);
            return updatedRecord;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteManyInternalAsync(Func<InternalMemoryRecord, bool> predicate, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var toDelete = _memoryIndex.Values.Where(predicate).Select(r => r.Id).ToList();

            foreach (var id in toDelete)
            {
                _memoryIndex.Remove(id);

                var operation = new WalOperation(
                    Op: WalOperationType.Delete,
                    Record: null,
                    Id: id,
                    Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );

                await AppendToWalAsync(operation, cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // ============================================================================
    // Archive Support
    // ============================================================================

    /// <summary>
    /// Moves records matching the predicate to the archive file.
    /// This is used for lifecycle management (TTL expiration, low importance cleanup).
    /// </summary>
    public async Task ArchiveAsync(Func<InternalMemoryRecord, bool> predicate, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var toArchive = _memoryIndex.Values.Where(predicate).ToList();

            if (toArchive.Count == 0)
                return;

            // Append to archive file
            await using (var writer = new StreamWriter(_archiveFilePath, append: true))
            {
                foreach (var record in toArchive)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(record));
                }
            }

            // Remove from active storage
            foreach (var record in toArchive)
            {
                _memoryIndex.Remove(record.Id);

                var operation = new WalOperation(
                    Op: WalOperationType.Delete,
                    Record: null,
                    Id: record.Id,
                    Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );

                await AppendToWalAsync(operation, cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Retrieves archived memories (read-only).
    /// </summary>
    public async Task<List<InternalMemoryRecord>> GetArchivedAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_archiveFilePath))
            return new List<InternalMemoryRecord>();

        var archived = new List<InternalMemoryRecord>();

        await foreach (var line in File.ReadLinesAsync(_archiveFilePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<InternalMemoryRecord>(line);
                if (record != null) archived.Add(record);
            }
            catch
            {
                // Skip corrupted lines
            }
        }

        return archived;
    }
}
