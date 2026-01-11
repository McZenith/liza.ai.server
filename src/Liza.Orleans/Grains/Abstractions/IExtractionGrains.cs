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

