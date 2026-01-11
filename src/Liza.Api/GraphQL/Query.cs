namespace Liza.Api.GraphQL;

using HotChocolate.Subscriptions;
using Liza.Core.Models;
using Liza.Core.Services;
using Liza.Orleans.Grains.Abstractions;

/// <summary>
/// GraphQL Query root - all read operations
/// </summary>
public class Query
{
    /// <summary>
    /// Perform comprehensive keyword research
    /// </summary>
    [GraphQLDescription("Perform full keyword research with parallel extraction")]
    public async Task<KeywordResearchResult> ResearchKeyword(
        [GraphQLDescription("The keyword to research")] string keyword,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IResearchOrchestratorGrain>(keyword);
        return await grain.ExecuteAsync();
    }

    /// <summary>
    /// Get trending videos from cache (warmed up daily)
    /// </summary>
    [GraphQLDescription("Get cached trending videos for a specific region")]
    public async Task<List<CachedTrendingVideo>> GetTrendingVideos(
        [GraphQLDescription("Region code (default US)")] string? regionCode,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<ITrendingAnalysisGrain>(regionCode ?? "US");
        return await grain.GetCachedTrendingVideosAsync();
    }

    /// <summary>
    /// Get pre-analyzed trending keywords (cached from daily warmup)
    /// </summary>
    [GraphQLDescription("Get pre-analyzed trending keywords for the Explore tab")]
    public async Task<List<TrendingKeywordSummary>> GetTrendingKeywords(
        [GraphQLDescription("Region code (default US)")] string? regionCode,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<ITrendingAnalysisGrain>(regionCode ?? "US");
        return await grain.GetCachedTrendingKeywordsAsync();
    }

    /// <summary>
    /// Search YouTube videos
    /// </summary>
    [GraphQLDescription("Search YouTube for videos")]
    public async Task<List<VideoData>> SearchVideos(
        [GraphQLDescription("Search query")] string query,
        [GraphQLDescription("Max results (default 50)")] int? maxResults,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IYouTubeSearchGrain>(query);
        return await grain.SearchAsync(maxResults ?? 50);
    }

    /// <summary>
    /// Get enriched video data
    /// </summary>
    [GraphQLDescription("Get full video data with transcript and comments")]
    public async Task<EnrichedVideo> GetVideo(
        [GraphQLDescription("YouTube video ID")] string videoId,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IVideoEnrichmentGrain>(videoId);
        return await grain.EnrichAsync();
    }

    /// <summary>
    /// Get channel details
    /// </summary>
    [GraphQLDescription("Get YouTube channel details")]
    public async Task<ChannelData> GetChannel(
        [GraphQLDescription("YouTube channel ID")] string channelId,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IChannelGrain>(channelId);
        return await grain.GetDetailsAsync();
    }

    /// <summary>
    /// Get recent videos from a channel
    /// </summary>
    [GraphQLDescription("Get recent videos from a channel")]
    public async Task<List<VideoData>> GetChannelVideos(
        [GraphQLDescription("YouTube channel ID")] string channelId,
        [GraphQLDescription("Number of videos (default 50)")] int? count,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IChannelGrain>(channelId);
        return await grain.GetRecentVideosAsync(count ?? 50);
    }

    /// <summary>
    /// Get autocomplete suggestions
    /// </summary>
    [GraphQLDescription("Get keyword autocomplete suggestions")]
    public async Task<AutocompleteSuggestions> GetAutocompleteSuggestions(
        [GraphQLDescription("Partial keyword")] string query,
        [Service] IGrainFactory grainFactory)
    {
        var ytGrain = grainFactory.GetGrain<IYouTubeAutocompleteGrain>(query);
        var googleGrain = grainFactory.GetGrain<IGoogleAutocompleteGrain>(query);
        
        var ytTask = ytGrain.GetSuggestionsAsync();
        var googleTask = googleGrain.GetSuggestionsAsync();
        
        await Task.WhenAll(ytTask, googleTask);
        
        return new AutocompleteSuggestions
        {
            YouTube = await ytTask,
            Google = await googleTask
        };
    }

    /// <summary>
    /// Get video transcript
    /// </summary>
    [GraphQLDescription("Get video transcript/captions")]
    public async Task<TranscriptData?> GetTranscript(
        [GraphQLDescription("YouTube video ID")] string videoId,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<ITranscriptGrain>(videoId);
        return await grain.GetTranscriptAsync();
    }

    /// <summary>
    /// Get video comments
    /// </summary>
    [GraphQLDescription("Get video comments")]
    public async Task<List<CommentData>> GetComments(
        [GraphQLDescription("YouTube video ID")] string videoId,
        [GraphQLDescription("Max comments (default 100)")] int? limit,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<ICommentsGrain>(videoId);
        return await grain.GetCommentsAsync(limit ?? 100);
    }

    /// <summary>
    /// Search Reddit posts
    /// </summary>
    [GraphQLDescription("Search Reddit for discussions about a topic")]
    public async Task<List<RedditPost>> SearchReddit(
        [GraphQLDescription("Search query")] string query,
        [GraphQLDescription("Max posts (default 25)")] int? limit,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IRedditGrain>(query);
        return await grain.SearchAsync(limit ?? 25);
    }

    /// <summary>
    /// Get Google Trends data
    /// </summary>
    [GraphQLDescription("Get Google Trends data for a keyword")]
    public async Task<TrendData> GetTrends(
        [GraphQLDescription("Keyword to analyze")] string keyword,
        [GraphQLDescription("Region code (default US)")] string? region,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IGoogleTrendsGrain>(keyword);
        return await grain.GetTrendsAsync(region ?? "US");
    }

    /// <summary>
    /// Search Google Video
    /// </summary>
    [GraphQLDescription("Search Google Video for related videos across platforms")]
    public async Task<List<GoogleVideoResult>> SearchGoogleVideo(
        [GraphQLDescription("Search query")] string query,
        [GraphQLDescription("Max results (default 10)")] int? limit,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IGoogleVideoSearchGrain>(query);
        return await grain.SearchAsync(limit ?? 10);
    }

    /// <summary>
    /// Get keyword metrics from Google Ads
    /// </summary>
    [GraphQLDescription("Get keyword metrics (search volume, competition, CPC) from Google Ads")]
    public async Task<KeywordData> GetKeywordMetrics(
        [GraphQLDescription("Keyword to analyze")] string keyword,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IKeywordPlannerGrain>(keyword);
        return await grain.GetMetricsAsync();
    }

    /// <summary>
    /// Get keyword ideas from Google Ads
    /// </summary>
    [GraphQLDescription("Get related keyword ideas from Google Ads Keyword Planner")]
    public async Task<List<KeywordData>> GetKeywordIdeas(
        [GraphQLDescription("Seed keyword")] string keyword,
        [GraphQLDescription("Max results (default 50)")] int? limit,
        [Service] IGrainFactory grainFactory)
    {
        var grain = grainFactory.GetGrain<IKeywordPlannerGrain>(keyword);
        return await grain.GetKeywordIdeasAsync(limit ?? 50);
    }

    /// <summary>
    /// Analyze a keyword with scoring and recommendations
    /// Auto-triggers long-tail analysis for ALL grades (subscribe to onLongTailAnalyzed)
    /// </summary>
    [GraphQLDescription("Analyze keyword with opportunity/difficulty scores and recommendations. Auto-triggers long-tail analysis.")]
    public async Task<KeywordAnalysisResult> AnalyzeKeyword(
        [GraphQLDescription("The keyword to analyze")] string keyword,
        [GraphQLDescription("Max long-tails to analyze (default 10)")] int? maxLongTails,
        [Service] IGrainFactory grainFactory,
        [Service] ITopicEventSender eventSender)
    {
        var grain = grainFactory.GetGrain<IKeywordAnalysisGrain>(keyword);
        var result = await grain.AnalyzeAsync();
        
        // Auto-trigger long-tail analysis for ALL grades (consistent UI)
        {
            var max = maxLongTails ?? 10;  // Default to 10 keywords
            
            // Fire and forget - stream results to subscription AS THEY COMPLETE
            _ = Task.Run(async () =>
            {
                try
                {
                    var count = 0;
                    var allResults = new List<LongTailResultSummary>();  // Track cumulative results
                    
                    // Use streaming method - each result is yielded immediately
                    await foreach (var lt in grain.StreamLongTailsAsync(max))
                    {
                        count++;
                        
                        // Add to cumulative list
                        allResults.Add(new LongTailResultSummary
                        {
                            Keyword = lt.LongTailKeyword,
                            Grade = lt.Grade,
                            Opportunity = lt.Opportunity,
                            Difficulty = lt.Difficulty,
                            SearchVolume = lt.SearchVolume,
                            Source = lt.Source
                        });
                        
                        var update = new LongTailAnalysisUpdate
                        {
                            ParentKeyword = keyword,
                            LongTailKeyword = lt.LongTailKeyword,
                            Opportunity = lt.Opportunity,
                            Difficulty = lt.Difficulty,
                            Grade = lt.Grade,
                            SearchVolume = lt.SearchVolume,
                            CompetitionLevel = lt.CompetitionLevel,
                            VideoCount = lt.VideoCount,
                            AvgCompetitorViews = lt.AvgCompetitorViews,
                            AnalyzedAt = lt.AnalyzedAt,
                            Source = lt.Source,
                            IsComplete = count >= max,
                            AnalyzedCount = count,
                            TotalCount = max,
                            AllResults = allResults.ToList()  // Send cumulative list with each update
                        };
                        
                        // Publish immediately to subscription
                        await eventSender.SendAsync(keyword, update);
                    }
                    
                    // Send final "complete" message if we got any results but less than max
                    if (count > 0 && count < max)
                    {
                        await eventSender.SendAsync(keyword, new LongTailAnalysisUpdate
                        {
                            ParentKeyword = keyword,
                            LongTailKeyword = "",
                            Opportunity = 0,
                            Difficulty = 0,
                            Grade = "",
                            SearchVolume = 0,
                            CompetitionLevel = "",
                            VideoCount = 0,
                            AvgCompetitorViews = 0,
                            AnalyzedAt = DateTime.UtcNow,
                            Source = "Complete",
                            IsComplete = true,
                            AnalyzedCount = count,
                            TotalCount = count,
                            AllResults = allResults  // Include final cumulative list
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in streaming long-tail analysis: {ex.Message}");
                }
            });
        }
        
        return result;
    }
}

/// <summary>
/// Combined autocomplete suggestions from multiple sources
/// </summary>
public class AutocompleteSuggestions
{
    public List<string> YouTube { get; set; } = [];
    public List<string> Google { get; set; } = [];
}
