namespace ClaudeMemoryApi.Configuration;

/// <summary>
/// Centralized configuration options for the Claude Memory API.
/// All settings have sensible defaults that preserve backward-compatible behavior.
/// </summary>
public class MemoryOptions
{
    // ============================================================================
    // Storage Configuration
    // ============================================================================

    /// <summary>
    /// Data directory for storage files.
    /// Default: C:\Temp\ai-memory (Windows) or /tmp/ai-memory (Linux/Mac)
    /// </summary>
    public string DataDirectory { get; set; } =
        OperatingSystem.IsWindows()
            ? @"C:\Temp\ai-memory"
            : "/tmp/ai-memory";

    /// <summary>
    /// Main data file name.
    /// Default: claude.jsonl (for backward compatibility)
    /// </summary>
    public string MainFileName { get; set; } = "claude.jsonl";

    /// <summary>
    /// Write-ahead log file name (used by WalMemoryStore).
    /// Default: wal.log
    /// </summary>
    public string WalFileName { get; set; } = "wal.log";

    /// <summary>
    /// Archive file name for expired/archived memories.
    /// Default: archive.jsonl
    /// </summary>
    public string ArchiveFileName { get; set; } = "archive.jsonl";

    /// <summary>
    /// Index file name for persisted index metadata (future use).
    /// Default: index.json
    /// </summary>
    public string IndexFileName { get; set; } = "index.json";

    /// <summary>
    /// Storage engine type: "simple" or "wal".
    /// - "simple": JsonLinesMemoryStore (backward compatible, single file)
    /// - "wal": WalMemoryStore (advanced, with WAL and archiving)
    /// Default: "simple" (for backward compatibility)
    /// </summary>
    public string StorageEngine { get; set; } = "simple";

    // ============================================================================
    // WAL Configuration (only used if StorageEngine == "wal")
    // ============================================================================

    /// <summary>
    /// Maximum WAL operations before compaction.
    /// Default: 1000
    /// </summary>
    public int WalMaxOperationsBeforeCompaction { get; set; } = 1000;

    /// <summary>
    /// Maximum WAL file size in bytes before compaction.
    /// Default: 5000000 (5 MB)
    /// </summary>
    public long WalMaxBytesBeforeCompaction { get; set; } = 5_000_000;

    // ============================================================================
    // Scoring Configuration
    // ============================================================================

    /// <summary>
    /// Weight for text similarity in scoring.
    /// Default: 1.0
    /// </summary>
    public double SimilarityWeight { get; set; } = 1.0;

    /// <summary>
    /// Weight for importance boost in scoring.
    /// Default: 0.5
    /// </summary>
    public double ImportanceWeight { get; set; } = 0.5;

    /// <summary>
    /// Weight for recency boost in scoring.
    /// Default: 0.3
    /// </summary>
    public double RecencyWeight { get; set; } = 0.3;

    /// <summary>
    /// Weight for access frequency boost in scoring.
    /// Default: 0.2
    /// </summary>
    public double AccessFrequencyWeight { get; set; } = 0.2;

    /// <summary>
    /// Weight for tag matching boost in scoring.
    /// Default: 0.3
    /// </summary>
    public double TagMatchWeight { get; set; } = 0.3;

    /// <summary>
    /// Recency half-life in days for exponential decay.
    /// Default: 30.0
    /// </summary>
    public double RecencyHalfLifeDays { get; set; } = 30.0;

    // ============================================================================
    // Deduplication Configuration
    // ============================================================================

    /// <summary>
    /// Enable or disable deduplication.
    /// Default: true
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Deduplication mode: "off", "mark", or "merge".
    /// Default: "mark"
    /// </summary>
    public string DeduplicationMode { get; set; } = "mark";

    /// <summary>
    /// Similarity threshold for deduplication (0.0 to 1.0).
    /// Default: 0.85
    /// </summary>
    public double DeduplicationThreshold { get; set; } = 0.85;

    // ============================================================================
    // Helper Properties
    // ============================================================================

    /// <summary>
    /// Gets the full path to the main data file.
    /// </summary>
    public string GetMainFilePath() =>
        Path.Combine(DataDirectory, MainFileName);

    /// <summary>
    /// Gets the full path to the WAL file.
    /// </summary>
    public string GetWalFilePath() =>
        Path.Combine(DataDirectory, WalFileName);

    /// <summary>
    /// Gets the full path to the archive file.
    /// </summary>
    public string GetArchiveFilePath() =>
        Path.Combine(DataDirectory, ArchiveFileName);

    /// <summary>
    /// Gets the full path to the index file.
    /// </summary>
    public string GetIndexFilePath() =>
        Path.Combine(DataDirectory, IndexFileName);
}
