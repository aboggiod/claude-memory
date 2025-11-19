using ClaudeMemoryApi.Models;
using ClaudeMemoryApi.Storage;
using System.Text;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Implementation of memory compression/summarization service.
/// Uses deterministic heuristics (no external LLM).
/// </summary>
public class CompressionService : ICompressionService
{
    private readonly IInternalMemoryStore _store;

    public CompressionService(IInternalMemoryStore store)
    {
        _store = store;
    }

    public async Task<MemoryRecord> CompressMemoriesAsync(
        CompressMemoriesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Ids == null || request.Ids.Count == 0)
        {
            throw new ArgumentException("At least one memory ID required for compression");
        }

        // Load the specified records
        var sourceRecords = new List<InternalMemoryRecord>();
        foreach (var id in request.Ids)
        {
            var record = await _store.GetInternalByIdAsync(id, cancellationToken);
            if (record != null)
            {
                sourceRecords.Add(record);
            }
        }

        if (sourceRecords.Count == 0)
        {
            throw new InvalidOperationException("No valid memories found for compression");
        }

        // Sort by timestamp (chronological)
        sourceRecords = sourceRecords.OrderBy(r => r.Timestamp).ToList();

        // Generate summary content using deterministic heuristics
        var summaryContent = GenerateSummary(sourceRecords, request.Context);

        // Aggregate tags (unique union)
        var aggregatedTags = sourceRecords
            .SelectMany(r => r.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Infer role (most common, or "assistant" as default)
        var inferredRole = sourceRecords
            .GroupBy(r => r.Role.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? "assistant";

        // Compute importance (max of source records, capped at request importance)
        var maxSourceImportance = sourceRecords.Max(r => r.Importance);
        var finalImportance = request.Importance ?? Math.Max(maxSourceImportance, 4);
        if (finalImportance < 1) finalImportance = 1;
        if (finalImportance > 5) finalImportance = 5;

        // Create summary record
        var summaryRecord = new InternalMemoryRecord(
            Id: Guid.NewGuid().ToString("N"),
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Role: inferredRole,
            Content: summaryContent,
            Tags: aggregatedTags,
            Context: request.Context,
            UserId: request.UserId,
            Namespace: request.Namespace,
            Collection: request.Collection,
            Kind: request.Kind ?? "summary",
            Importance: finalImportance,
            SourceIds: request.Ids
        );

        // Save the summary
        await _store.AddInternalAsync(summaryRecord, cancellationToken);

        // Return as external MemoryRecord
        return summaryRecord.ToMemoryRecord();
    }

    /// <summary>
    /// Generates a deterministic summary from source records.
    /// Uses bullet-point format with timestamps and snippets.
    /// </summary>
    private string GenerateSummary(List<InternalMemoryRecord> records, string? context)
    {
        var summary = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(context))
        {
            summary.AppendLine($"Summary: {context}");
            summary.AppendLine();
        }
        else
        {
            summary.AppendLine($"Summary of {records.Count} memories:");
            summary.AppendLine();
        }

        foreach (var record in records)
        {
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(record.Timestamp);
            var dateStr = timestamp.ToString("yyyy-MM-dd HH:mm");

            // Extract first sentence or truncate content
            var snippet = ExtractSnippet(record.Content, maxLength: 120);

            summary.AppendLine($"• [{dateStr}] {record.Role}: {snippet}");

            // Include tags if present
            if (record.Tags.Count > 0)
            {
                summary.AppendLine($"  Tags: {string.Join(", ", record.Tags)}");
            }
        }

        return summary.ToString().Trim();
    }

    /// <summary>
    /// Extracts a snippet from content (first sentence or truncated).
    /// </summary>
    private string ExtractSnippet(string content, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "(empty)";

        content = content.Trim();

        // Try to find first sentence
        var sentenceEnds = new[] { ". ", "! ", "? ", "\n" };
        var firstSentenceEnd = -1;

        foreach (var ending in sentenceEnds)
        {
            var index = content.IndexOf(ending);
            if (index > 0 && (firstSentenceEnd == -1 || index < firstSentenceEnd))
            {
                firstSentenceEnd = index + 1;
            }
        }

        if (firstSentenceEnd > 0 && firstSentenceEnd <= maxLength)
        {
            return content.Substring(0, firstSentenceEnd).Trim();
        }

        // Truncate and add ellipsis
        if (content.Length > maxLength)
        {
            return content.Substring(0, maxLength - 3).Trim() + "...";
        }

        return content;
    }
}
