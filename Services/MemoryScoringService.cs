using ClaudeMemoryApi.Models;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Service for scoring and ranking memories based on multiple factors.
/// </summary>
public class MemoryScoringService
{
    private readonly ScoringOptions _options;

    public MemoryScoringService(ScoringOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Computes a comprehensive score for a memory record.
    /// </summary>
    public double ComputeScore(
        InternalMemoryRecord record,
        Dictionary<string, int>? recordBow,
        Dictionary<string, int>? queryBow,
        HashSet<string>? queryTags)
    {
        double score = 0.0;

        // 1. Similarity score (if query text provided)
        if (recordBow != null && queryBow != null)
        {
            var similarity = TextTokenizer.CosineSimilarity(recordBow, queryBow);
            score += similarity * _options.SimilarityWeight;
        }

        // 2. Importance boost
        // Importance ranges from 1-5, normalize to 0-1 and scale
        var importanceBoost = ((record.Importance - 1) / 4.0) * _options.ImportanceWeight;
        score += importanceBoost;

        // 3. Recency boost (exponential decay)
        var ageInMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - record.Timestamp;
        var ageInDays = ageInMilliseconds / (1000.0 * 60 * 60 * 24);
        var recencyBoost = Math.Exp(-ageInDays / _options.RecencyHalfLifeDays) * _options.RecencyWeight;
        score += recencyBoost;

        // 4. Access frequency boost
        var accessBoost = Math.Log(1 + record.AccessCount) * _options.AccessFrequencyWeight;
        score += accessBoost;

        // 5. Tag match boost
        if (queryTags != null && queryTags.Count > 0)
        {
            var recordTagsLower = new HashSet<string>(record.Tags.Select(t => t.ToLowerInvariant()));
            var matchingTags = recordTagsLower.Intersect(queryTags).Count();
            if (matchingTags > 0)
            {
                score += _options.TagMatchWeight;
            }
        }

        return score;
    }

    /// <summary>
    /// Scores and ranks a list of records.
    /// </summary>
    public List<InternalMemoryRecord> ScoreAndRank(
        List<InternalMemoryRecord> records,
        MemoryIndex index,
        string? queryText,
        HashSet<string>? queryTags)
    {
        Dictionary<string, int>? queryBow = null;
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            queryBow = TextTokenizer.CreateBagOfWords(queryText);
        }

        var scored = records.Select(record =>
        {
            var recordBow = index.GetBagOfWords(record.Id);
            var score = ComputeScore(record, recordBow, queryBow, queryTags);
            return new { Record = record, Score = score };
        })
        .OrderByDescending(x => x.Score)
        .Select(x => x.Record)
        .ToList();

        return scored;
    }
}
