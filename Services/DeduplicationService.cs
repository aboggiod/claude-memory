using ClaudeMemoryApi.Models;
using ClaudeMemoryApi.Storage;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Implementation of deduplication service.
/// Detects near-duplicate memories and handles them according to configured policy.
/// </summary>
public class DeduplicationService : IDeduplicationService
{
    private readonly IInternalMemoryStore _store;
    private readonly DeduplicationOptions _options;

    public DeduplicationService(IInternalMemoryStore store, DeduplicationOptions options)
    {
        _store = store;
        _options = options;
    }

    public async Task<(InternalMemoryRecord? BestMatch, double Similarity)> FindDuplicateAsync(
        InternalMemoryRecord newRecord,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableDeduplication)
            return (null, 0.0);

        // Get all records in the same scope
        var allRecords = await _store.GetAllInternalAsync(cancellationToken);

        var candidates = allRecords.Where(r =>
        {
            // Match by userId, namespace, collection (if specified)
            if (!string.IsNullOrWhiteSpace(newRecord.UserId) &&
                !string.Equals(r.UserId, newRecord.UserId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(newRecord.Namespace) &&
                !string.Equals(r.Namespace, newRecord.Namespace, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(newRecord.Collection) &&
                !string.Equals(r.Collection, newRecord.Collection, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }).ToList();

        if (candidates.Count == 0)
            return (null, 0.0);

        // Compute similarity for each candidate
        var newContent = $"{newRecord.Content} {newRecord.Context ?? ""}";
        var newBow = TextTokenizer.CreateBagOfWords(newContent);

        InternalMemoryRecord? bestMatch = null;
        double bestSimilarity = 0.0;

        foreach (var candidate in candidates)
        {
            var candidateContent = $"{candidate.Content} {candidate.Context ?? ""}";
            var candidateBow = TextTokenizer.CreateBagOfWords(candidateContent);

            var similarity = TextTokenizer.CosineSimilarity(newBow, candidateBow);

            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestMatch = candidate;
            }
        }

        // Check if best match exceeds threshold
        if (bestSimilarity >= _options.DeduplicationThreshold)
        {
            return (bestMatch, bestSimilarity);
        }

        return (null, 0.0);
    }

    public async Task<InternalMemoryRecord> HandleDeduplicationAsync(
        InternalMemoryRecord newRecord,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableDeduplication || _options.DeduplicationMode == "off")
        {
            // No deduplication - just add the new record
            return await _store.AddInternalAsync(newRecord, cancellationToken);
        }

        var (bestMatch, similarity) = await FindDuplicateAsync(newRecord, cancellationToken);

        if (bestMatch == null)
        {
            // No duplicate found - add new record
            return await _store.AddInternalAsync(newRecord, cancellationToken);
        }

        // Duplicate found - handle according to mode
        switch (_options.DeduplicationMode.ToLowerInvariant())
        {
            case "mark":
                // Create new record but mark it as duplicate
                var markedRecord = newRecord with
                {
                    DuplicateOfId = bestMatch.Id
                };
                return await _store.AddInternalAsync(markedRecord, cancellationToken);

            case "merge":
                // Update existing record instead of creating new
                var mergedTags = bestMatch.Tags.Union(newRecord.Tags).Distinct().ToList();
                var mergedRecord = bestMatch with
                {
                    Tags = mergedTags,
                    AccessCount = bestMatch.AccessCount + 1,
                    LastAccessedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _store.UpdateInternalAsync(bestMatch.Id, mergedRecord, cancellationToken);
                // Return the merged record (with existing ID)
                return mergedRecord;

            default:
                // Unknown mode - default to adding new record
                return await _store.AddInternalAsync(newRecord, cancellationToken);
        }
    }
}
