using System.Text.Json.Serialization;

namespace ClaudeMemoryApi.Models;

/// <summary>
/// Internal memory record with extended metadata for advanced features.
/// This model is backward-compatible with MemoryRecord - missing fields will use defaults.
///
/// Extended fields:
/// - UserId: for multi-user support
/// - Namespace: for logical partitioning
/// - Collection: for grouping related memories
/// - Kind: type of memory (fact, event, summary, preference, log)
/// - Importance: priority/weight (1-5, default 3)
/// - ExpiresAtUnixMs: TTL expiration timestamp
/// - LastAccessedUnixMs: tracking for recency scoring
/// - AccessCount: tracking for frequency scoring
/// - SourceIds: for summary/derived memories (references to original IDs)
/// - DuplicateOfId: for deduplication tracking
/// </summary>
public record InternalMemoryRecord(
    // Original fields (backward-compatible with MemoryRecord)
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tags")] List<string> Tags,
    [property: JsonPropertyName("context")] string? Context,

    // Extended fields (new in Phase 2)
    [property: JsonPropertyName("userId")] string? UserId = null,
    [property: JsonPropertyName("namespace")] string? Namespace = null,
    [property: JsonPropertyName("collection")] string? Collection = null,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("importance")] int Importance = 3,
    [property: JsonPropertyName("expiresAtUnixMs")] long? ExpiresAtUnixMs = null,
    [property: JsonPropertyName("lastAccessedUnixMs")] long? LastAccessedUnixMs = null,
    [property: JsonPropertyName("accessCount")] int AccessCount = 0,
    [property: JsonPropertyName("sourceIds")] List<string>? SourceIds = null,
    [property: JsonPropertyName("duplicateOfId")] string? DuplicateOfId = null
)
{
    /// <summary>
    /// Determines if this record is pinned (never auto-archived).
    /// A record is pinned if its importance is 5.
    /// </summary>
    [JsonIgnore]
    public bool IsPinned => Importance >= 5;

    /// <summary>
    /// Determines if this record has expired based on TTL.
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => ExpiresAtUnixMs.HasValue &&
                              DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > ExpiresAtUnixMs.Value;

    /// <summary>
    /// Creates an InternalMemoryRecord from a MemoryRecord (for backward compatibility).
    /// </summary>
    public static InternalMemoryRecord FromMemoryRecord(MemoryRecord record)
    {
        return new InternalMemoryRecord(
            Id: record.Id,
            Timestamp: record.Timestamp,
            Role: record.Role,
            Content: record.Content,
            Tags: record.Tags,
            Context: record.Context
        );
    }

    /// <summary>
    /// Converts to external MemoryRecord format (for API responses).
    /// </summary>
    public MemoryRecord ToMemoryRecord()
    {
        return new MemoryRecord(
            Id: Id,
            Timestamp: Timestamp,
            Role: Role,
            Content: Content,
            Tags: Tags,
            Context: Context
        );
    }

    /// <summary>
    /// Creates a copy with access tracking updated.
    /// </summary>
    public InternalMemoryRecord WithAccessTracking()
    {
        return this with
        {
            LastAccessedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessCount = AccessCount + 1
        };
    }
}
