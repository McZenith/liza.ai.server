namespace Liza.Orleans.Grains.Abstractions;

using Liza.Core.Models;

/// <summary>
/// Orchestrates a complete keyword research across all sources
/// </summary>
public interface IResearchOrchestratorGrain : IGrainWithStringKey
{
    /// <summary>
    /// Execute full keyword research in parallel
    /// </summary>
    Task<KeywordResearchResult> ExecuteAsync();
    
    /// <summary>
    /// Stream partial results as they complete
    /// </summary>
    IAsyncEnumerable<PartialResearchResult> StreamAsync();
}

/// <summary>
/// Partial result from streaming research
/// </summary>
public class PartialResearchResult
{
    public required string Source { get; init; }
    public required object Data { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Searches YouTube for videos matching a keyword
/// </summary>
public interface IYouTubeSearchGrain : IGrainWithStringKey
{
    Task<List<VideoData>> SearchAsync(int maxResults = 50);
}

/// <summary>
/// Gets autocomplete suggestions from YouTube
/// </summary>
public interface IYouTubeAutocompleteGrain : IGrainWithStringKey
{
    Task<List<string>> GetSuggestionsAsync();
}

/// <summary>
/// Gets autocomplete suggestions from Google
/// </summary>
public interface IGoogleAutocompleteGrain : IGrainWithStringKey
{
    Task<List<string>> GetSuggestionsAsync();
}

/// <summary>
/// Gets trend data from Google Trends
/// </summary>
public interface IGoogleTrendsGrain : IGrainWithStringKey
{
    Task<TrendData> GetTrendsAsync(string region = "US");
}

/// <summary>
/// Searches Reddit for relevant posts
/// </summary>
public interface IRedditGrain : IGrainWithStringKey
{
    Task<List<RedditPost>> SearchAsync(int limit = 25);
}

/// <summary>
/// Enriches a single video with all available data
/// </summary>
public interface IVideoEnrichmentGrain : IGrainWithStringKey
{
    /// <summary>Full enrichment including transcript (slower, ~2-3s)</summary>
    Task<EnrichedVideo> EnrichAsync();
    
    /// <summary>Fast enrichment without transcript (~500ms)</summary>
    Task<EnrichedVideo> EnrichFastAsync();
}

/// <summary>
/// Gets channel details and metadata
/// </summary>
public interface IChannelGrain : IGrainWithStringKey
{
    Task<ChannelData> GetDetailsAsync();
    Task<List<VideoData>> GetRecentVideosAsync(int count = 50);
}

/// <summary>
/// Extracts transcript/captions from a video
/// </summary>
public interface ITranscriptGrain : IGrainWithStringKey
{
    Task<TranscriptData?> GetTranscriptAsync();
}

/// <summary>
/// Gets comments from a video
/// </summary>
public interface ICommentsGrain : IGrainWithStringKey
{
    Task<List<CommentData>> GetCommentsAsync(int limit = 100);
}

/// <summary>
/// Searches Google Video for related videos
/// </summary>
public interface IGoogleVideoSearchGrain : IGrainWithStringKey
{
    Task<List<GoogleVideoResult>> SearchAsync(int limit = 10);
}

/// <summary>
/// Gets keyword metrics from Google Ads Keyword Planner
/// </summary>
public interface IKeywordPlannerGrain : IGrainWithStringKey
{
    Task<KeywordData> GetMetricsAsync();
    Task<List<KeywordData>> GetKeywordIdeasAsync(int limit = 50);
}

/// <summary>
/// Analyzes a keyword with scoring, ranking factors, and recommendations
/// </summary>
public interface IKeywordAnalysisGrain : IGrainWithStringKey
{
    Task<KeywordAnalysisResult> AnalyzeAsync();
    
    /// <summary>
    /// Analyze top long-tail variations and return results
    /// </summary>
    Task<List<LongTailAnalyzedResult>> AnalyzeLongTailsAsync(int maxVariations);
    
    /// <summary>
    /// Stream long-tail analysis results as they complete (one by one)
    /// </summary>
    IAsyncEnumerable<LongTailAnalyzedResult> StreamLongTailsAsync(int maxVariations);
}

/// <summary>
/// Manages trending keyword analysis and caching for the Explore tab
/// Warmed up daily by the scheduled worker
/// </summary>
public interface ITrendingAnalysisGrain : IGrainWithStringKey
{
    /// <summary>
    /// Warm up the cache by fetching trending videos and analyzing extracted keywords.
    /// Called daily by the scheduled worker at 6 AM UTC.
    /// </summary>
    Task WarmupAsync();
    
    /// <summary>
    /// Get the cached trending keywords from the last warmup.
    /// Returns quickly since data is pre-analyzed.
    /// </summary>
    Task<List<TrendingKeywordSummary>> GetCachedTrendingKeywordsAsync();
    
    /// <summary>
    /// Get the cached trending videos from the last warmup.
    /// Returns quickly since data is pre-cached.
    /// </summary>
    Task<List<CachedTrendingVideo>> GetCachedTrendingVideosAsync();
}

/// <summary>
/// Cached trending video with essential display fields
/// </summary>
[GenerateSerializer]
public class CachedTrendingVideo
{
    [Id(0)] public required string VideoId { get; init; }
    [Id(1)] public required string Title { get; init; }
    [Id(2)] public required string ChannelTitle { get; init; }
    [Id(3)] public long ViewCount { get; init; }
    [Id(4)] public long LikeCount { get; init; }
    [Id(5)] public string? ThumbnailMedium { get; init; }
    [Id(6)] public string? ThumbnailHigh { get; init; }
    [Id(7)] public DateTime PublishedAt { get; init; }
}

/// <summary>
/// Summary of a trending keyword with pre-analyzed metrics
/// </summary>
[GenerateSerializer]
public class TrendingKeywordSummary
{
    [Id(0)] public required string Keyword { get; init; }
    [Id(1)] public required string Grade { get; init; }
    [Id(2)] public int Opportunity { get; init; }
    [Id(3)] public int Difficulty { get; init; }
    [Id(4)] public long SearchVolume { get; init; }
    [Id(5)] public int TrendingVideoCount { get; init; }
    [Id(6)] public string? TopVideoTitle { get; init; }
    [Id(7)] public string? TopVideoThumbnail { get; init; }
    [Id(8)] public DateTime AnalyzedAt { get; init; }
}
