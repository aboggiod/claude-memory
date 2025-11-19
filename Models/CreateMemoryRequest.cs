using System.Text.Json.Serialization;

namespace ClaudeMemoryApi.Models;

/// <summary>
/// Request model for creating a new memory.
/// </summary>
public record CreateMemoryRequest(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tags")] List<string>? Tags,
    [property: JsonPropertyName("context")] string? Context
);
