namespace Liza.Orleans.Grains;

using Liza.Core.Models;
using Liza.Core.Services;
using Liza.Infrastructure.Search;
using Liza.Orleans.Grains.Abstractions;

/// <summary>
/// Reddit search grain implementation
/// </summary>
public class RedditGrain : Grain, IRedditGrain
{
    private readonly IRedditService _redditService;

    public RedditGrain(IRedditService redditService)
    {
        _redditService = redditService;
    }

    public Task<List<RedditPost>> SearchAsync(int limit = 25)
    {
        var query = this.GetPrimaryKeyString();
        return _redditService.SearchPostsAsync(query, limit);
    }
}

/// <summary>
/// Google Trends grain implementation
/// </summary>
public class GoogleTrendsGrain : Grain, IGoogleTrendsGrain
{
    private readonly IGoogleTrendsService _trendsService;

    public GoogleTrendsGrain(IGoogleTrendsService trendsService)
    {
        _trendsService = trendsService;
    }

    public Task<TrendData> GetTrendsAsync(string region = "US")
    {
        var keyword = this.GetPrimaryKeyString();
        return _trendsService.GetTrendsAsync(keyword, region);
    }
}

/// <summary>
/// Google Video Search grain implementation
/// </summary>
public class GoogleVideoSearchGrain : Grain, IGoogleVideoSearchGrain
{
    private readonly IGoogleVideoSearchService _videoSearchService;

    public GoogleVideoSearchGrain(IGoogleVideoSearchService videoSearchService)
    {
        _videoSearchService = videoSearchService;
    }

    public Task<List<GoogleVideoResult>> SearchAsync(int limit = 10)
    {
        var query = this.GetPrimaryKeyString();
        return _videoSearchService.SearchVideosAsync(query, limit);
    }
}

/// <summary>
/// Keyword Planner grain implementation
/// </summary>
public class KeywordPlannerGrain : Grain, IKeywordPlannerGrain
{
    private readonly IKeywordPlannerService _keywordPlannerService;

    public KeywordPlannerGrain(IKeywordPlannerService keywordPlannerService)
    {
        _keywordPlannerService = keywordPlannerService;
    }

    public Task<KeywordData> GetMetricsAsync()
    {
        var keyword = this.GetPrimaryKeyString();
        return _keywordPlannerService.GetKeywordMetricsAsync(keyword);
    }

    public Task<List<KeywordData>> GetKeywordIdeasAsync(int limit = 50)
    {
        var keyword = this.GetPrimaryKeyString();
        return _keywordPlannerService.GetKeywordIdeasAsync(keyword, limit);
    }
}
