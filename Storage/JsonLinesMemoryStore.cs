using ClaudeMemoryApi.Models;
using System.Text.Json;

namespace ClaudeMemoryApi.Storage;

/// <summary>
/// File-based implementation of IMemoryStore and IInternalMemoryStore using JSONL (JSON Lines) format.
/// Preserves exact behavior of the original storage mechanism, including:
/// - JSONL file format (one JSON object per line)
/// - SemaphoreSlim for concurrency control
/// - Atomic file replacement for updates
/// - Graceful handling of corrupted lines
///
/// Phase 2 Enhancement:
/// - Now stores InternalMemoryRecord internally with extended metadata
/// - Backward compatible: can read both old MemoryRecord and new InternalMemoryRecord formats
/// - External API continues to use MemoryRecord for backward compatibility
/// </summary>
public class JsonLinesMemoryStore : IMemoryStore, IInternalMemoryStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileSemaphore;

    public JsonLinesMemoryStore(string filePath)
    {
        _filePath = EnsureDirectoryExists(filePath);
        _fileSemaphore = new SemaphoreSlim(1, 1);
    }

    private static string EnsureDirectoryExists(string path)
    {
        var dir = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Invalid path");
        Directory.CreateDirectory(dir);
        return path;
    }

    public async Task<MemoryRecord> AddAsync(MemoryRecord record, CancellationToken cancellationToken = default)
    {
        // Convert to internal format and add
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
        // Get existing internal record to preserve extended metadata
        var existingInternal = await GetInternalByIdAsync(id);
        if (existingInternal == null)
            return null;

        // Create updated internal record, preserving extended fields
        var updatedInternal = existingInternal with
        {
            Role = updatedRecord.Role,
            Content = updatedRecord.Content,
            Tags = updatedRecord.Tags,
            Context = updatedRecord.Context
        };

        await UpdateInternalAsync(id, updatedInternal);
        return updatedRecord;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var internalRecords = await GetAllInternalAsync();
        var toRemove = internalRecords.FirstOrDefault(r =>
            string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

        if (toRemove == null)
            return false;

        internalRecords.Remove(toRemove);
        await WriteAllInternalRecordsAsync(internalRecords);
        return true;
    }

    public async Task DeleteManyAsync(Func<MemoryRecord, bool> predicate, CancellationToken cancellationToken = default)
    {
        await DeleteManyInternalAsync(r => predicate(r.ToMemoryRecord()), cancellationToken);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _fileSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    // ============================================================================
    // IInternalMemoryStore Implementation
    // ============================================================================

    public async Task<InternalMemoryRecord> AddInternalAsync(InternalMemoryRecord record, CancellationToken cancellationToken = default)
    {
        await _fileSemaphore.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_filePath, JsonSerializer.Serialize(record) + "\n", cancellationToken);
            return record;
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    public async Task<InternalMemoryRecord?> GetInternalByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var records = await GetAllInternalAsync(cancellationToken);
        return records.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<InternalMemoryRecord>> GetAllInternalAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return new List<InternalMemoryRecord>();

        await _fileSemaphore.WaitAsync(cancellationToken);
        try
        {
            var records = new List<InternalMemoryRecord>();
            await foreach (var line in File.ReadLinesAsync(_filePath, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    // Try to deserialize as InternalMemoryRecord
                    // This handles both old MemoryRecord format (missing fields use defaults)
                    // and new InternalMemoryRecord format
                    var record = JsonSerializer.Deserialize<InternalMemoryRecord>(line);
                    if (record != null) records.Add(record);
                }
                catch
                {
                    // Skip corrupted lines - preserving original behavior
                }
            }
            return records;
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    public async Task<InternalMemoryRecord?> UpdateInternalAsync(string id, InternalMemoryRecord updatedRecord, CancellationToken cancellationToken = default)
    {
        var records = await GetAllInternalAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(r =>
            string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

        if (existingRecord == null)
            return null;

        records[records.IndexOf(existingRecord)] = updatedRecord;
        await WriteAllInternalRecordsAsync(records, cancellationToken);
        return updatedRecord;
    }

    public async Task DeleteManyInternalAsync(Func<InternalMemoryRecord, bool> predicate, CancellationToken cancellationToken = default)
    {
        var records = await GetAllInternalAsync(cancellationToken);
        var filtered = records.Where(r => !predicate(r)).ToList();
        await WriteAllInternalRecordsAsync(filtered, cancellationToken);
    }

    // ============================================================================
    // Private Helpers
    // ============================================================================

    private async Task WriteAllInternalRecordsAsync(IEnumerable<InternalMemoryRecord> records, CancellationToken cancellationToken = default)
    {
        var tempPath = _filePath + ".tmp";

        await _fileSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var writer = new StreamWriter(tempPath, false);
            foreach (var record in records)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(record));
            }
        }
        finally
        {
            _fileSemaphore.Release();
        }

        // Atomic file replacement
        File.Replace(tempPath, _filePath, null);
    }
}
