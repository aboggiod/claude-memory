namespace ClaudeMemoryApi.Services;

/// <summary>
/// Configuration options for memory deduplication.
/// </summary>
public class DeduplicationOptions
{
    /// <summary>
    /// Enable or disable deduplication.
    /// Default: true
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Deduplication mode:
    /// - "off": No deduplication (always create new record)
    /// - "mark": Create new record but mark it as duplicate
    /// - "merge": Update existing record instead of creating new
    /// Default: "mark"
    /// </summary>
    public string DeduplicationMode { get; set; } = "mark";

    /// <summary>
    /// Similarity threshold for considering two memories as duplicates.
    /// Range: 0.0 to 1.0
    /// Default: 0.85
    /// </summary>
    public double DeduplicationThreshold { get; set; } = 0.85;
}
