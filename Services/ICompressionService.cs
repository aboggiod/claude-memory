using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Interface for memory compression/summarization operations.
/// </summary>
public interface ICompressionService
{
    /// <summary>
    /// Compresses multiple memories into a single summarized memory.
    /// No external LLM calls - uses deterministic heuristics.
    /// </summary>
    Task<MemoryRecord> CompressMemoriesAsync(
        CompressMemoriesRequest request,
        CancellationToken cancellationToken = default);
}
