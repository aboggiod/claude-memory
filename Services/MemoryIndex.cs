using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// In-memory index for fast memory lookups and searches.
/// Builds inverted indexes for text search and metadata filtering.
/// </summary>
public class MemoryIndex
{
    // Primary index: ID -> Record
    private readonly Dictionary<string, InternalMemoryRecord> _recordsById;

    // Metadata indexes
    private readonly Dictionary<string, HashSet<string>> _recordsByUserId; // UserId -> Record IDs
    private readonly Dictionary<string, HashSet<string>> _recordsByNamespace; // Namespace -> Record IDs
    private readonly Dictionary<string, HashSet<string>> _recordsByCollection; // Collection -> Record IDs
    private readonly Dictionary<string, HashSet<string>> _recordsByRole; // Role -> Record IDs
    private readonly Dictionary<string, HashSet<string>> _recordsByTag; // Tag (lowercase) -> Record IDs

    // Inverted text index: Token -> Record IDs
    private readonly Dictionary<string, HashSet<string>> _invertedIndex;

    // Bag-of-words cache for scoring
    private readonly Dictionary<string, Dictionary<string, int>> _bowCache;

    public MemoryIndex()
    {
        _recordsById = new Dictionary<string, InternalMemoryRecord>(StringComparer.OrdinalIgnoreCase);
        _recordsByUserId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _recordsByNamespace = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _recordsByCollection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _recordsByRole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _recordsByTag = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _invertedIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _bowCache = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rebuilds the entire index from a list of records.
    /// </summary>
    public void RebuildIndex(List<InternalMemoryRecord> records)
    {
        Clear();

        foreach (var record in records)
        {
            AddToIndex(record);
        }
    }

    /// <summary>
    /// Adds a single record to the index.
    /// </summary>
    public void AddToIndex(InternalMemoryRecord record)
    {
        // Primary index
        _recordsById[record.Id] = record;

        // Metadata indexes
        if (!string.IsNullOrWhiteSpace(record.UserId))
        {
            AddToMultiValueIndex(_recordsByUserId, record.UserId, record.Id);
        }

        if (!string.IsNullOrWhiteSpace(record.Namespace))
        {
            AddToMultiValueIndex(_recordsByNamespace, record.Namespace, record.Id);
        }

        if (!string.IsNullOrWhiteSpace(record.Collection))
        {
            AddToMultiValueIndex(_recordsByCollection, record.Collection, record.Id);
        }

        AddToMultiValueIndex(_recordsByRole, record.Role, record.Id);

        foreach (var tag in record.Tags)
        {
            AddToMultiValueIndex(_recordsByTag, tag.ToLowerInvariant(), record.Id);
        }

        // Text index
        var combinedText = $"{record.Content} {record.Context ?? ""}";
        var tokens = TextTokenizer.Tokenize(combinedText);
        var bow = TextTokenizer.CreateBagOfWords(combinedText);

        _bowCache[record.Id] = bow;

        foreach (var token in tokens.Distinct())
        {
            AddToMultiValueIndex(_invertedIndex, token, record.Id);
        }
    }

    /// <summary>
    /// Removes a record from the index.
    /// </summary>
    public void RemoveFromIndex(string id)
    {
        if (!_recordsById.TryGetValue(id, out var record))
            return;

        _recordsById.Remove(id);

        // Remove from metadata indexes
        if (!string.IsNullOrWhiteSpace(record.UserId))
        {
            RemoveFromMultiValueIndex(_recordsByUserId, record.UserId, id);
        }

        if (!string.IsNullOrWhiteSpace(record.Namespace))
        {
            RemoveFromMultiValueIndex(_recordsByNamespace, record.Namespace, id);
        }

        if (!string.IsNullOrWhiteSpace(record.Collection))
        {
            RemoveFromMultiValueIndex(_recordsByCollection, record.Collection, id);
        }

        RemoveFromMultiValueIndex(_recordsByRole, record.Role, id);

        foreach (var tag in record.Tags)
        {
            RemoveFromMultiValueIndex(_recordsByTag, tag.ToLowerInvariant(), id);
        }

        // Remove from text index
        var combinedText = $"{record.Content} {record.Context ?? ""}";
        var tokens = TextTokenizer.Tokenize(combinedText).Distinct();

        foreach (var token in tokens)
        {
            RemoveFromMultiValueIndex(_invertedIndex, token, id);
        }

        _bowCache.Remove(id);
    }

    /// <summary>
    /// Updates a record in the index.
    /// </summary>
    public void UpdateInIndex(InternalMemoryRecord record)
    {
        RemoveFromIndex(record.Id);
        AddToIndex(record);
    }

    /// <summary>
    /// Clears the entire index.
    /// </summary>
    public void Clear()
    {
        _recordsById.Clear();
        _recordsByUserId.Clear();
        _recordsByNamespace.Clear();
        _recordsByCollection.Clear();
        _recordsByRole.Clear();
        _recordsByTag.Clear();
        _invertedIndex.Clear();
        _bowCache.Clear();
    }

    /// <summary>
    /// Gets a record by ID.
    /// </summary>
    public InternalMemoryRecord? GetById(string id)
    {
        return _recordsById.TryGetValue(id, out var record) ? record : null;
    }

    /// <summary>
    /// Gets all records.
    /// </summary>
    public List<InternalMemoryRecord> GetAll()
    {
        return _recordsById.Values.ToList();
    }

    /// <summary>
    /// Gets records matching text tokens (for search).
    /// </summary>
    public HashSet<string> GetRecordIdsByTokens(List<string> tokens)
    {
        if (tokens.Count == 0)
            return new HashSet<string>();

        var results = new HashSet<string>();

        foreach (var token in tokens)
        {
            if (_invertedIndex.TryGetValue(token, out var recordIds))
            {
                foreach (var id in recordIds)
                {
                    results.Add(id);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Gets the bag-of-words representation for a record.
    /// </summary>
    public Dictionary<string, int>? GetBagOfWords(string id)
    {
        return _bowCache.TryGetValue(id, out var bow) ? bow : null;
    }

    // Helper methods

    private static void AddToMultiValueIndex(Dictionary<string, HashSet<string>> index, string key, string value)
    {
        if (!index.TryGetValue(key, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            index[key] = set;
        }
        set.Add(value);
    }

    private static void RemoveFromMultiValueIndex(Dictionary<string, HashSet<string>> index, string key, string value)
    {
        if (index.TryGetValue(key, out var set))
        {
            set.Remove(value);
            if (set.Count == 0)
            {
                index.Remove(key);
            }
        }
    }
}
