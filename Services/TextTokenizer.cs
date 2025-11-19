using System.Text.RegularExpressions;

namespace ClaudeMemoryApi.Services;

/// <summary>
/// Simple, deterministic text tokenizer for indexing and search.
/// No external NLP dependencies - just basic text processing.
/// </summary>
public static class TextTokenizer
{
    private static readonly Regex TokenRegex = new Regex(@"[a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Tokenizes text into lowercase alphanumeric tokens.
    /// Splits on non-alphanumeric characters and removes empty tokens.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var normalized = text.ToLowerInvariant();
        var matches = TokenRegex.Matches(normalized);

        return matches
            .Select(m => m.Value)
            .Where(token => !string.IsNullOrEmpty(token))
            .ToList();
    }

    /// <summary>
    /// Creates a bag-of-words representation with term frequencies.
    /// Returns a dictionary of token -> frequency.
    /// </summary>
    public static Dictionary<string, int> CreateBagOfWords(string text)
    {
        var tokens = Tokenize(text);
        var bagOfWords = new Dictionary<string, int>();

        foreach (var token in tokens)
        {
            bagOfWords.TryGetValue(token, out var count);
            bagOfWords[token] = count + 1;
        }

        return bagOfWords;
    }

    /// <summary>
    /// Computes Jaccard similarity between two sets of tokens.
    /// Returns a value between 0.0 (no overlap) and 1.0 (identical).
    /// </summary>
    public static double JaccardSimilarity(HashSet<string> set1, HashSet<string> set2)
    {
        if (set1.Count == 0 && set2.Count == 0)
            return 1.0;

        if (set1.Count == 0 || set2.Count == 0)
            return 0.0;

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();

        return (double)intersection / union;
    }

    /// <summary>
    /// Computes a simple cosine-like similarity between two bag-of-words representations.
    /// Returns a value between 0.0 (no similarity) and 1.0 (identical).
    /// </summary>
    public static double CosineSimilarity(Dictionary<string, int> bow1, Dictionary<string, int> bow2)
    {
        if (bow1.Count == 0 && bow2.Count == 0)
            return 1.0;

        if (bow1.Count == 0 || bow2.Count == 0)
            return 0.0;

        // Compute dot product
        double dotProduct = 0.0;
        foreach (var kvp in bow1)
        {
            if (bow2.TryGetValue(kvp.Key, out var freq2))
            {
                dotProduct += kvp.Value * freq2;
            }
        }

        // Compute magnitudes
        var magnitude1 = Math.Sqrt(bow1.Values.Sum(v => v * v));
        var magnitude2 = Math.Sqrt(bow2.Values.Sum(v => v * v));

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0.0;

        return dotProduct / (magnitude1 * magnitude2);
    }
}
