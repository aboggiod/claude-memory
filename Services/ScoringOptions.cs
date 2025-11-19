namespace ClaudeMemoryApi.Services;

/// <summary>
/// Configuration options for memory scoring and ranking.
/// </summary>
public class ScoringOptions
{
    /// <summary>
    /// Weight for text similarity in the final score.
    /// Default: 1.0
    /// </summary>
    public double SimilarityWeight { get; set; } = 1.0;

    /// <summary>
    /// Weight for importance boost in the final score.
    /// Default: 0.5
    /// </summary>
    public double ImportanceWeight { get; set; } = 0.5;

    /// <summary>
    /// Weight for recency boost in the final score.
    /// Default: 0.3
    /// </summary>
    public double RecencyWeight { get; set; } = 0.3;

    /// <summary>
    /// Weight for access frequency boost in the final score.
    /// Default: 0.2
    /// </summary>
    public double AccessFrequencyWeight { get; set} = 0.2;

    /// <summary>
    /// Weight for tag matching boost in the final score.
    /// Default: 0.3
    /// </summary>
    public double TagMatchWeight { get; set; } = 0.3;

    /// <summary>
    /// Recency half-life in days. Older memories decay exponentially.
    /// Default: 30 days
    /// </summary>
    public double RecencyHalfLifeDays { get; set; } = 30.0;
}
