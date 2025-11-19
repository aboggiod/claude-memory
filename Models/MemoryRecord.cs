using System.Text.Json.Serialization;

namespace ClaudeMemoryApi.Models;

/// <summary>
/// Public-facing memory record model. This is the external contract and MUST NOT be changed
/// in breaking ways to preserve backward compatibility with existing clients.
/// </summary>
public record MemoryRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tags")] List<string> Tags,
    [property: JsonPropertyName("context")] string? Context
);
