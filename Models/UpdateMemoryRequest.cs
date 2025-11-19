using System.Text.Json.Serialization;

namespace ClaudeMemoryApi.Models;

/// <summary>
/// Request model for updating an existing memory. All fields are optional for partial updates.
/// </summary>
public record UpdateMemoryRequest(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tags")] List<string>? Tags,
    [property: JsonPropertyName("context")] string? Context
);
