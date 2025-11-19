using System.Text.Json.Serialization;

namespace ClaudeMemoryApi.Models;

/// <summary>
/// Request model for compressing/summarizing multiple memories into one.
/// </summary>
public record CompressMemoriesRequest(
    [property: JsonPropertyName("ids")] List<string> Ids,
    [property: JsonPropertyName("userId")] string? UserId = null,
    [property: JsonPropertyName("namespace")] string? Namespace = null,
    [property: JsonPropertyName("collection")] string? Collection = null,
    [property: JsonPropertyName("kind")] string? Kind = "summary",
    [property: JsonPropertyName("importance")] int? Importance = 4,
    [property: JsonPropertyName("context")] string? Context = null
);
