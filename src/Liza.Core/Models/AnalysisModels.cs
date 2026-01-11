namespace Liza.Core.Models;

/// <summary>
/// Search demand pattern classification
/// </summary>
public enum TrendType
{
    Seasonal,   // Peaks at same time each year
    Trending,   // Rising momentum recently
    Consistent, // Stable interest over time
    Declining   // Falling momentum
}

/// <summary>
/// Competition level classification
/// </summary>
public enum CompetitionLevel
{
    Low,
    Medium,
    High
}

/// <summary>
/// Search demand analysis result
/// </summary>
[GenerateSerializer]
public class SearchDemand
{
    [Id(0)] public int Volume { get; init; }
    [Id(1)] public TrendType TrendType { get; init; }
    [Id(2)] public int Momentum { get; init; }  // -100 to +100
    [Id(3)] public string? SeasonalPeak { get; init; }  // e.g., "December" for holiday terms
}

/// <summary>
/// Content supply/competition analysis
/// </summary>
[GenerateSerializer]
public class ContentSupply
{
    [Id(0)] public int VideoCount { get; init; }  // Enriched video count
    [Id(1)] public double ContentGapScore { get; init; }  // > 1.0 = opportunity
    [Id(2)] public CompetitionLevel CompetitionLevel { get; init; }
    [Id(3)] public long AvgCompetitorViews { get; init; }
    [Id(4)] public long AvgCompetitorSubscribers { get; init; }
    
    // Total videos from search (not just enriched)
    [Id(5)] public int TotalSearchResults { get; init; }
    
    // Video velocity metrics (upload frequency)
    [Id(6)] public int VideosUploadedToday { get; init; }
    [Id(7)] public int VideosLast3Days { get; init; }
    [Id(8)] public int VideosThisWeek { get; init; }
    [Id(9)] public int VideosThisMonth { get; init; }
    [Id(10)] public int VideosThisYear { get; init; }
    
    // Dormant opportunity: high search volume but low recent content activity
    [Id(11)] public bool IsDormantOpportunity { get; init; }
    [Id(12)] public string ContentActivityLevel { get; init; } = "Unknown"; // Hot, Active, Moderate, Slow, Dormant
}

/// <summary>
/// Individual ranking factor with correlation
/// </summary>
[GenerateSerializer]
public class RankingFactor
{
    [Id(0)] public required string Factor { get; init; }
    [Id(1)] public double Correlation { get; init; }  // -1 to +1
    [Id(2)] public string Recommendation { get; init; } = "";
}

/// <summary>
/// Channel authority analysis
/// </summary>
[GenerateSerializer]
public class ChannelAuthority
{
    [Id(0)] public long AvgSubscribers { get; init; }
    [Id(1)] public bool NeedsEstablishedChannel { get; init; }
    [Id(2)] public int MinSubscribersToRank { get; init; }
}

/// <summary>
/// Ranking factor insights
/// </summary>
[GenerateSerializer]
public class RankingInsights
{
    [Id(0)] public List<RankingFactor> TopFactors { get; init; } = [];
    [Id(1)] public ChannelAuthority ChannelAuthority { get; init; } = new();
    [Id(2)] public KeywordPlacement OptimalPlacement { get; init; } = new();
    [Id(3)] public List<LongTailVariation> LongTailVariations { get; init; } = [];
}

/// <summary>
/// Long-tail keyword variation with competition analysis
/// </summary>
[GenerateSerializer]
public class LongTailVariation
{
    [Id(0)] public required string Keyword { get; init; }
    [Id(1)] public string Source { get; init; } = "";  // YouTube, Google, Extracted
    [Id(2)] public int EstimatedDifficulty { get; init; }  // 0-100, lower = easier
    [Id(3)] public string Reason { get; init; } = "";  // Why this variation is good
}

/// <summary>
/// Optimal keyword placement analysis
/// </summary>
[GenerateSerializer]
public class KeywordPlacement
{
    [Id(0)] public bool InTitleFirst3Words { get; init; }
    [Id(1)] public bool InDescriptionFirst100Chars { get; init; }
    [Id(2)] public int OptimalTagCount { get; init; }
    [Id(3)] public double TranscriptMentionDensity { get; init; }  // mentions per minute
}

/// <summary>
/// Opportunity and difficulty scores
/// </summary>
[GenerateSerializer]
public class KeywordScores
{
    [Id(0)] public int Opportunity { get; init; }  // 0-100
    [Id(1)] public int Difficulty { get; init; }   // 0-100
    [Id(2)] public string Grade { get; init; } = "C";  // A, B, C, D, F
}

/// <summary>
/// Content recommendations based on top performers
/// </summary>
[GenerateSerializer]
public class ContentRecommendations
{
    [Id(0)] public List<string> TitlePatterns { get; init; } = [];
    [Id(1)] public List<string> MustHaveTags { get; init; } = [];
    [Id(2)] public int OptimalLengthSeconds { get; init; }
    [Id(3)] public List<string> TopQuestions { get; init; } = [];
    [Id(4)] public List<string> RelatedKeywords { get; init; } = [];
}

/// <summary>
/// Extracted keyword with frequency analysis
/// </summary>
[GenerateSerializer]
public class ExtractedKeyword
{
    [Id(0)] public required string Keyword { get; init; }
    [Id(1)] public int TotalCount { get; init; }
    [Id(2)] public double TfIdfScore { get; init; }
    [Id(3)] public KeywordSourceBreakdown Sources { get; init; } = new();
}

/// <summary>
/// Where keywords appear across content
/// </summary>
[GenerateSerializer]
public record KeywordSourceBreakdown
{
    [Id(0)] public int TitleCount { get; init; }
    [Id(1)] public int DescriptionCount { get; init; }
    [Id(2)] public int TagCount { get; init; }
    [Id(3)] public int TranscriptCount { get; init; }
    [Id(4)] public int CommentCount { get; init; }
}

/// <summary>
/// YouTube recommendation algorithm optimization insights
/// </summary>
[GenerateSerializer]
public class RecommendationOptimization
{
    [Id(0)] public List<TagOverlap> TagOverlaps { get; init; } = [];
    [Id(1)] public List<string> MustUseTagsForRecommendation { get; init; } = [];
    [Id(2)] public List<TopicCluster> TopicClusters { get; init; } = [];
    [Id(3)] public List<TargetVideo> VideosToAppearAlongside { get; init; } = [];
    [Id(4)] public int TopicMatchScore { get; init; }  // 0-100
    [Id(5)] public List<string> TranscriptKeywordsToMention { get; init; } = [];
}

/// <summary>
/// Tag overlap analysis with top videos
/// </summary>
[GenerateSerializer]
public class TagOverlap
{
    [Id(0)] public required string Tag { get; init; }
    [Id(1)] public int UsageCount { get; init; }  // How many top videos use this tag
    [Id(2)] public double AvgViewsWithTag { get; init; }
}

/// <summary>
/// Topic cluster from content analysis
/// </summary>
[GenerateSerializer]
public class TopicCluster
{
    [Id(0)] public required string Topic { get; init; }
    [Id(1)] public int Frequency { get; init; }
    [Id(2)] public List<string> RelatedTerms { get; init; } = [];
}

/// <summary>
/// Target video for recommendation association
/// </summary>
[GenerateSerializer]
public class TargetVideo
{
    [Id(0)] public required string VideoId { get; init; }
    [Id(1)] public required string Title { get; init; }
    [Id(2)] public long ViewCount { get; init; }
    [Id(3)] public List<string> SharedTags { get; init; } = [];
    [Id(4)] public int SimilarityScore { get; init; }  // 0-100
}

/// <summary>
/// Full keyword analysis result
/// </summary>
[GenerateSerializer]
public class KeywordAnalysisResult
{
    [Id(0)] public required string Keyword { get; init; }
    [Id(1)] public DateTime AnalyzedAt { get; init; } = DateTime.UtcNow;
    [Id(2)] public SearchDemand SearchDemand { get; init; } = new();
    [Id(3)] public ContentSupply ContentSupply { get; init; } = new();
    [Id(4)] public RankingInsights RankingInsights { get; init; } = new();
    [Id(5)] public KeywordScores Scores { get; init; } = new();
    [Id(6)] public ContentRecommendations Recommendations { get; init; } = new();
    [Id(7)] public List<ExtractedKeyword> TopExtractedKeywords { get; init; } = [];
    [Id(8)] public RecommendationOptimization RecommendationOptimization { get; init; } = new();
    [Id(9)] public List<EnrichedVideo> TopVideos { get; init; } = [];  // Top 5 videos with ranking signals
}

/// <summary>
/// Result of analyzing a long-tail keyword variation
/// </summary>
[GenerateSerializer]
public class LongTailAnalyzedResult
{
    [Id(0)] public required string LongTailKeyword { get; init; }
    [Id(1)] public required string Source { get; init; }  // YouTube/Google/Extracted
    [Id(2)] public int Opportunity { get; init; }
    [Id(3)] public int Difficulty { get; init; }
    [Id(4)] public required string Grade { get; init; }
    [Id(5)] public long SearchVolume { get; init; }
    [Id(6)] public required string CompetitionLevel { get; init; }
    [Id(7)] public int VideoCount { get; init; }
    [Id(8)] public long AvgCompetitorViews { get; init; }
    [Id(9)] public DateTime AnalyzedAt { get; init; } = DateTime.UtcNow;
}
