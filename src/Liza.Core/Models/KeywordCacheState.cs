namespace Liza.Core.Models;

/// <summary>
/// State for caching keyword analysis results in MongoDB
/// </summary>
[GenerateSerializer]
public class KeywordCacheState
{
    [Id(0)] public KeywordAnalysisResult? CachedResult { get; set; }
    [Id(1)] public DateTime CachedAt { get; set; }
    [Id(2)] public List<LongTailAnalyzedResult> CachedLongTails { get; set; } = [];
    [Id(3)] public DateTime LongTailsCachedAt { get; set; }
    
    /// <summary>
    /// Check if main analysis cache is still valid (24 hours)
    /// </summary>
    public bool IsAnalysisValid => CachedResult != null && 
        DateTime.UtcNow - CachedAt < TimeSpan.FromHours(24);
    
    /// <summary>
    /// Check if long-tails cache is still valid (24 hours)
    /// </summary>
    public bool AreLongTailsValid => CachedLongTails.Count > 0 && 
        DateTime.UtcNow - LongTailsCachedAt < TimeSpan.FromHours(24);
}
