namespace Liza.Core.Services;

using Liza.Core.Models;

/// <summary>
/// Service for fetching data from YouTube Data API v3
/// </summary>
public interface IYouTubeService
{
    /// <summary>
    /// Search for videos by keyword
    /// </summary>
    Task<List<VideoData>> SearchVideosAsync(string query, int maxResults = 50);
    
    /// <summary>
    /// Get details for a single video
    /// </summary>
    Task<VideoData?> GetVideoDetailsAsync(string videoId);
    
    /// <summary>
    /// Get details for multiple videos (batch)
    /// </summary>
    Task<List<VideoData>> GetVideoDetailsBatchAsync(IEnumerable<string> videoIds);
    
    /// <summary>
    /// Get channel details
    /// </summary>
    Task<ChannelData?> GetChannelDetailsAsync(string channelId);
    
    /// <summary>
    /// Get recent videos from a channel
    /// </summary>
    Task<List<VideoData>> GetChannelVideosAsync(string channelId, int maxResults = 50);
    
    /// <summary>
    /// Get comments from a video
    /// </summary>
    Task<List<CommentData>> GetVideoCommentsAsync(string videoId, int maxResults = 100);
    
    /// <summary>
    /// Get trending videos
    /// </summary>
    Task<List<VideoData>> GetTrendingVideosAsync(string regionCode = "US", string? categoryId = null);
}

/// <summary>
/// Service for scraping YouTube transcripts (no API cost)
/// </summary>
public interface ITranscriptService
{
    /// <summary>
    /// Get transcript for a video
    /// </summary>
    Task<TranscriptData?> GetTranscriptAsync(string videoId, string language = "en");
}

/// <summary>
/// Service for fetching autocomplete suggestions
/// </summary>
public interface IAutocompleteService
{
    /// <summary>
    /// Get YouTube autocomplete suggestions
    /// </summary>
    Task<List<string>> GetYouTubeSuggestionsAsync(string query);
    
    /// <summary>
    /// Get Google autocomplete suggestions
    /// </summary>
    Task<List<string>> GetGoogleSuggestionsAsync(string query);
}

/// <summary>
/// Service for Google Trends data
/// </summary>
public interface IGoogleTrendsService
{
    /// <summary>
    /// Get trend data for a keyword
    /// </summary>
    Task<TrendData> GetTrendsAsync(string keyword, string region = "US");
    
    /// <summary>
    /// Get trend data from YouTube specifically (not web search)
    /// </summary>
    Task<TrendData> GetYouTubeTrendsAsync(string keyword, string region = "US");
    
    /// <summary>
    /// Get rising queries from YouTube trends (fast-growing YouTube searches)
    /// </summary>
    Task<List<string>> GetYouTubeRisingQueriesAsync(string keyword);
    
    /// <summary>
    /// Get related queries for a keyword
    /// </summary>
    Task<List<string>> GetRelatedQueriesAsync(string keyword);
    
    /// <summary>
    /// Get rising queries (fast-growing searches)
    /// </summary>
    Task<List<string>> GetRisingQueriesAsync(string keyword);
}

/// <summary>
/// Service for Reddit data
/// </summary>
public interface IRedditService
{
    /// <summary>
    /// Search posts across Reddit
    /// </summary>
    Task<List<RedditPost>> SearchPostsAsync(string query, int limit = 25);
    
    /// <summary>
    /// Search in specific subreddits
    /// </summary>
    Task<List<RedditPost>> SearchSubredditAsync(string subreddit, string query, int limit = 25);
}

/// <summary>
/// Service for Google Ads keyword data
/// </summary>
public interface IKeywordPlannerService
{
    /// <summary>
    /// Get search volume and competition for keywords
    /// </summary>
    Task<KeywordData> GetKeywordMetricsAsync(string keyword);
    
    /// <summary>
    /// Get related keyword ideas
    /// </summary>
    Task<List<KeywordData>> GetKeywordIdeasAsync(string seedKeyword, int limit = 50);
}
