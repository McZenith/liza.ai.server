namespace Liza.Orleans.Grains;

using Liza.Core.Models;
using Liza.Core.Services;
using Liza.Infrastructure.Analysis;
using Liza.Orleans.Grains.Abstractions;
using Microsoft.Extensions.Logging;
using global::Orleans.Runtime;

/// <summary>
/// Orleans grain that orchestrates keyword analysis
/// Combines research data with scoring and recommendations
/// </summary>
public class KeywordAnalysisGrain : Grain, IKeywordAnalysisGrain
{
    private readonly IGrainFactory _grainFactory;
    private readonly ISearchDemandService _searchDemandService;
    private readonly IContentGapService _contentGapService;
    private readonly IRankingFactorService _rankingFactorService;
    private readonly IKeywordScoreService _keywordScoreService;
    private readonly IKeywordExtractionService _keywordExtractionService;
    private readonly IRecommendationOptimizationService _recommendationOptimizationService;
    private readonly IGoogleTrendsService _trendsService;
    private readonly IKeywordPlannerService _keywordPlannerService;
    private readonly ILogger<KeywordAnalysisGrain> _logger;
    private readonly IPersistentState<KeywordCacheState> _cache;

    public KeywordAnalysisGrain(
        [PersistentState("keywordCache", "KeywordCache")]
        IPersistentState<KeywordCacheState> cache,
        IGrainFactory grainFactory,
        ISearchDemandService searchDemandService,
        IContentGapService contentGapService,
        IRankingFactorService rankingFactorService,
        IKeywordScoreService keywordScoreService,
        IKeywordExtractionService keywordExtractionService,
        IRecommendationOptimizationService recommendationOptimizationService,
        IGoogleTrendsService trendsService,
        IKeywordPlannerService keywordPlannerService,
        ILogger<KeywordAnalysisGrain> logger)
    {
        _cache = cache;
        _grainFactory = grainFactory;
        _searchDemandService = searchDemandService;
        _contentGapService = contentGapService;
        _rankingFactorService = rankingFactorService;
        _keywordScoreService = keywordScoreService;
        _keywordExtractionService = keywordExtractionService;
        _recommendationOptimizationService = recommendationOptimizationService;
        _trendsService = trendsService;
        _keywordPlannerService = keywordPlannerService;
        _logger = logger;
    }

    public async Task<KeywordAnalysisResult> AnalyzeAsync()
    {
        var keyword = this.GetPrimaryKeyString();
        
        // Check cache first - return if valid (< 24 hours old)
        if (_cache.State.IsAnalysisValid)
        {
            _logger.LogInformation("Returning cached analysis for: {Keyword} (cached at {CachedAt})", 
                keyword, _cache.State.CachedAt);
            return _cache.State.CachedResult!;
        }
        
        _logger.LogInformation("Starting fresh keyword analysis for: {Keyword}", keyword);
        
        // Step 1: Get research data (uses cached or executes fresh)
        var researchGrain = _grainFactory.GetGrain<IResearchOrchestratorGrain>(keyword);
        var research = await researchGrain.ExecuteAsync();
        
        // Step 2: Get trends data
        var trendData = await _trendsService.GetTrendsAsync(keyword);
        
        // Step 3: Extract keywords first (needed for long-tail variations)
        var extractedKeywords = await _keywordExtractionService.ExtractAsync(keyword, research.Videos);
        
        // Step 4: Run analysis in parallel (ranking now gets suggestions + keywords for long-tail)
        var demandTask = _searchDemandService.AnalyzeAsync(keyword, trendData, research.KeywordMetrics);
        var supplyTask = _contentGapService.AnalyzeAsync(keyword, research.Videos, research.KeywordMetrics, research.TotalVideoCount);
        var rankingTask = _rankingFactorService.AnalyzeAsync(
            keyword, 
            research.Videos,
            research.YouTubeSuggestions,
            research.GoogleSuggestions,
            extractedKeywords);
        var recommendationOptTask = _recommendationOptimizationService.AnalyzeAsync(keyword, research.Videos);
        
        await Task.WhenAll(demandTask, supplyTask, rankingTask, recommendationOptTask);
        
        var demand = await demandTask;
        var supply = await supplyTask;
        var ranking = await rankingTask;
        var recommendationOptimization = await recommendationOptTask;
        
        // Step 4: Calculate scores
        var scores = await _keywordScoreService.CalculateAsync(demand, supply, ranking);
        
        // Step 5: Generate recommendations
        var recommendations = await _keywordScoreService.GenerateRecommendationsAsync(
            keyword, research.Videos, research.KeywordMetrics, trendData);
        
        // Step 6: Calculate ranking signals for top 5 videos
        // First, fetch channel videos for unique channels in parallel
        var top5 = research.Videos.Take(5).ToList();
        var uniqueChannelIds = top5
            .Where(v => v.Channel != null)
            .Select(v => v.Channel!.ChannelId)
            .Distinct()
            .ToList();
        
        // Fetch recent videos for each channel in parallel (for keyword authority analysis)
        var channelVideoTasks = uniqueChannelIds.ToDictionary(
            channelId => channelId,
            channelId => _grainFactory.GetGrain<IChannelGrain>(channelId).GetRecentVideosAsync(50)
        );
        
        await Task.WhenAll(channelVideoTasks.Values);
        
        var channelVideosLookup = channelVideoTasks.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Result
        );
        
        // Now calculate signals with channel videos
        var topVideos = top5
            .Select(v =>
            {
                List<VideoData>? channelVideos = null;
                if (v.Channel != null && channelVideosLookup.TryGetValue(v.Channel.ChannelId, out var videos))
                {
                    channelVideos = videos;
                }
                
                return new EnrichedVideo
                {
                    Details = v.Details,
                    Transcript = v.Transcript,
                    Comments = v.Comments,
                    Channel = v.Channel,
                    RankingSignals = _rankingFactorService.CalculateVideoSignals(keyword, v, channelVideos)
                };
            })
            .ToList();
        
        // Step 7: Calculate channel keyword authority correlation (after we have all video signals)
        var authorityFactor = _rankingFactorService.AnalyzeChannelKeywordAuthority(topVideos);
        
        // Add the authority factor to ranking insights (create new instance since RankingInsights is a class)
        var enhancedFactors = ranking.TopFactors
            .Append(authorityFactor)
            .OrderByDescending(f => Math.Abs(f.Correlation))
            .Take(6)  // Now include up to 6 factors
            .ToList();
        
        var enhancedRanking = new RankingInsights
        {
            TopFactors = enhancedFactors,
            OptimalPlacement = ranking.OptimalPlacement,
            ChannelAuthority = ranking.ChannelAuthority,
            LongTailVariations = ranking.LongTailVariations
        };
        
        _logger.LogInformation(
            "Analysis complete for {Keyword}: Opportunity={Opportunity}, Difficulty={Difficulty}, Grade={Grade}", 
            keyword, scores.Opportunity, scores.Difficulty, scores.Grade);
        
        var result = new KeywordAnalysisResult
        {
            Keyword = keyword,
            AnalyzedAt = DateTime.UtcNow,
            SearchDemand = demand,
            ContentSupply = supply,
            RankingInsights = enhancedRanking,
            Scores = scores,
            Recommendations = recommendations,
            TopExtractedKeywords = extractedKeywords.Take(20).ToList(),
            RecommendationOptimization = recommendationOptimization,
            TopVideos = topVideos
        };
        
        // Cache the result for future requests
        _cache.State.CachedResult = result;
        _cache.State.CachedAt = DateTime.UtcNow;
        await _cache.WriteStateAsync();
        _logger.LogInformation("Cached analysis result for: {Keyword}", keyword);
        
        return result;
    }

    public async Task<List<LongTailAnalyzedResult>> AnalyzeLongTailsAsync(int maxVariations)
    {
        var parentKeyword = this.GetPrimaryKeyString();
        
        // Check cache first - return if valid (< 24 hours old)
        if (_cache.State.AreLongTailsValid)
        {
            _logger.LogInformation("Returning cached long-tails for: {Keyword} (cached at {CachedAt})", 
                parentKeyword, _cache.State.LongTailsCachedAt);
            return _cache.State.CachedLongTails;
        }
        
        _logger.LogInformation("Starting fresh long-tail analysis for: {Keyword}", parentKeyword);
        
        // Get seed words for relevance matching
        var seedWords = parentKeyword.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet();
        
        // Step 1: Get trending keywords from YouTube Trends (YouTube-specific data!)
        var youtubeTrends = await _trendsService.GetYouTubeTrendsAsync(parentKeyword);
        var youtubeCandidates = new List<string>();
        youtubeCandidates.AddRange(youtubeTrends.RelatedQueries);
        youtubeCandidates.AddRange(youtubeTrends.RisingQueries);
        
        _logger.LogInformation("YouTube Trends: {Related} related, {Rising} rising queries", 
            youtubeTrends.RelatedQueries.Count, youtubeTrends.RisingQueries.Count);
        
        // Step 2: Get Google Ads data for metrics (volume/competition)
        var googleAdsKeywords = await _keywordPlannerService.GetKeywordIdeasAsync(parentKeyword, 50);
        
        // Combine: YouTube trends + Google Ads low-competition keywords
        var allCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add YouTube trends candidates (filtered for relevance)
        foreach (var candidate in youtubeCandidates.Where(c => IsRelatedKeyword(c, parentKeyword, seedWords)))
        {
            allCandidates.Add(candidate);
        }
        
        // Add Google Ads low-competition candidates (filtered for relevance)
        foreach (var kw in googleAdsKeywords.Where(k => 
            (k.Competition == "low" || k.CompetitionIndex <= 40) && 
            IsRelatedKeyword(k.Keyword, parentKeyword, seedWords)))
        {
            allCandidates.Add(kw.Keyword);
        }
        
        _logger.LogInformation(
            "Combined {Count} unique candidate keywords for: {Keyword}", 
            allCandidates.Count, parentKeyword);
        
        if (!allCandidates.Any())
        {
            _logger.LogWarning("No candidate keywords found for: {Keyword}", parentKeyword);
            return [];
        }
        
        // Limit to analyze at least 15 to find good ones
        var toAnalyze = allCandidates.Take(Math.Max(maxVariations, 15)).ToList();
        
        var results = new List<LongTailAnalyzedResult>();
        
        // Create lookup for Google Ads metrics
        var adsMetrics = googleAdsKeywords.ToDictionary(k => k.Keyword.ToLowerInvariant(), k => k);
        
        // Analyze in batches of 3 to avoid rate limiting
        const int batchSize = 3;
        var batches = toAnalyze
            .Select((item, index) => new { item, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();
        
        foreach (var batch in batches)
        {
            _logger.LogInformation("Analyzing batch of {Count} keywords", batch.Count);
            
            var batchTasks = batch.Select(async keyword =>
            {
                try
                {
                    // Full YouTube analysis on this keyword
                    var grain = _grainFactory.GetGrain<IKeywordAnalysisGrain>(keyword);
                    var result = await grain.AnalyzeAsync();
                    
                    // Try to get Google Ads metrics for this keyword
                    var hasAdsData = adsMetrics.TryGetValue(keyword.ToLowerInvariant(), out var adsData);
                    var isFromYoutubeTrends = youtubeCandidates.Contains(keyword, StringComparer.OrdinalIgnoreCase);
                    
                    return new LongTailAnalyzedResult
                    {
                        LongTailKeyword = keyword,
                        Source = isFromYoutubeTrends ? "YouTube Trends" : "Google Ads",
                        Opportunity = result.Scores.Opportunity,
                        Difficulty = result.Scores.Difficulty,
                        Grade = result.Scores.Grade,
                        SearchVolume = hasAdsData ? adsData!.SearchVolume : 0,
                        CompetitionLevel = result.ContentSupply.CompetitionLevel.ToString(),
                        VideoCount = result.ContentSupply.VideoCount,
                        AvgCompetitorViews = result.ContentSupply.AvgCompetitorViews,
                        AnalyzedAt = DateTime.UtcNow
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to analyze: {Keyword}", keyword);
                    return null;
                }
            });
            
            var batchResults = await Task.WhenAll(batchTasks);
            results.AddRange(batchResults.Where(r => r != null)!);
            
            // Delay between batches to avoid rate limiting
            if (batch != batches.Last())
            {
                await Task.Delay(2000);
            }
        }
        
        // Return only A/B/C grade keywords, sorted by best first
        var goodResults = results
            .Where(r => r!.Grade is "A" or "B" or "C")  // Only high-quality opportunities
            .OrderByDescending(r => r!.Opportunity - r!.Difficulty)
            .ThenBy(r => r!.Difficulty)
            .Take(maxVariations)  // Limit to requested count
            .ToList()!;
        
        _logger.LogInformation(
            "Smart long-tail analysis complete: {Total} analyzed, {Good} with A/B/C grades", 
            results.Count, goodResults.Count);
        
        // Cache the results for future requests
        _cache.State.CachedLongTails = goodResults;
        _cache.State.LongTailsCachedAt = DateTime.UtcNow;
        await _cache.WriteStateAsync();
        _logger.LogInformation("Cached long-tail results for: {Keyword}", parentKeyword);
        
        return goodResults;
    }

    /// <summary>
    /// Stream long-tail analysis results as they complete (one by one)
    /// Yields each result immediately after analysis
    /// </summary>
    public async IAsyncEnumerable<LongTailAnalyzedResult> StreamLongTailsAsync(int maxVariations)
    {
        var parentKeyword = this.GetPrimaryKeyString();
        _logger.LogInformation("Starting STREAMING long-tail analysis for: {Keyword}", parentKeyword);
        
        // Get seed words for relevance matching
        var seedWords = parentKeyword.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet();
        
        // Step 1: Get trending keywords from YouTube Trends
        var youtubeTrends = await _trendsService.GetYouTubeTrendsAsync(parentKeyword);
        var youtubeCandidates = new List<string>();
        youtubeCandidates.AddRange(youtubeTrends.RelatedQueries);
        youtubeCandidates.AddRange(youtubeTrends.RisingQueries);
        
        // Step 2: Get Google Ads data for metrics - fetch 100 to ensure we get 10 good ones
        var googleAdsKeywords = await _keywordPlannerService.GetKeywordIdeasAsync(parentKeyword, 100);
        var adsMetrics = googleAdsKeywords.ToDictionary(k => k.Keyword.ToLowerInvariant(), k => k);
        
        // For single-word seeds, be more lenient - include ALL Google Ads results as candidates
        var isSingleWord = !parentKeyword.Contains(' ');
        
        // Combine candidates - relax filtering based on seed type
        var allCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add YouTube trends candidates (filtered for relevance)
        foreach (var candidate in youtubeCandidates.Where(c => IsRelatedKeyword(c, parentKeyword, seedWords)))
            allCandidates.Add(candidate);
        
        // For single-word seeds: include ALL low/medium competition keywords from Google Ads
        // For multi-word seeds: require relevance check
        foreach (var kw in googleAdsKeywords.Where(k => 
            (k.Competition == "low" || k.Competition == "medium" || k.CompetitionIndex <= 60) && 
            (isSingleWord || IsRelatedKeyword(k.Keyword, parentKeyword, seedWords))))
            allCandidates.Add(kw.Keyword);
        
        // FALLBACK: If we still don't have enough, include high-competition keywords too
        if (allCandidates.Count < maxVariations * 3)
        {
            foreach (var kw in googleAdsKeywords.Where(k => 
                k.Competition == "high" || k.CompetitionIndex > 60))
            {
                if (isSingleWord || IsRelatedKeyword(kw.Keyword, parentKeyword, seedWords))
                    allCandidates.Add(kw.Keyword);
            }
        }
        
        // Analyze at least 3x candidates to find enough good ones (A/B/C/D grades)
        var toAnalyze = allCandidates.Take(maxVariations * 3).ToList();
        _logger.LogInformation("Streaming {Count} candidates for: {Keyword} (target: {Target} good results)", 
            toAnalyze.Count, parentKeyword, maxVariations);
        
        var yielded = 0;
        
        // Analyze ONE AT A TIME and yield immediately
        foreach (var keyword in toAnalyze)
        {
            LongTailAnalyzedResult? result = null;
            try
            {
                var grain = _grainFactory.GetGrain<IKeywordAnalysisGrain>(keyword);
                var analysisResult = await grain.AnalyzeAsync();
                
                // Only yield good grades (A/B/C)
                if (analysisResult.Scores.Grade is "A" or "B" or "C")
                {
                    var hasAdsData = adsMetrics.TryGetValue(keyword.ToLowerInvariant(), out var adsData);
                    var isFromYoutube = youtubeCandidates.Contains(keyword, StringComparer.OrdinalIgnoreCase);
                    
                    result = new LongTailAnalyzedResult
                    {
                        LongTailKeyword = keyword,
                        Source = isFromYoutube ? "YouTube Trends" : "Google Ads",
                        Opportunity = analysisResult.Scores.Opportunity,
                        Difficulty = analysisResult.Scores.Difficulty,
                        Grade = analysisResult.Scores.Grade,
                        SearchVolume = hasAdsData ? adsData!.SearchVolume : 0,
                        CompetitionLevel = analysisResult.ContentSupply.CompetitionLevel.ToString(),
                        VideoCount = analysisResult.ContentSupply.VideoCount,
                        AvgCompetitorViews = analysisResult.ContentSupply.AvgCompetitorViews,
                        AnalyzedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze: {Keyword}", keyword);
            }
            
            // Yield immediately if we got a result
            if (result != null)
            {
                yielded++;
                _logger.LogInformation("Streaming result {Count}: {Keyword} Grade={Grade}", 
                    yielded, result.LongTailKeyword, result.Grade);
                yield return result;
                
                // Stop if we've yielded enough good results
                if (yielded >= maxVariations)
                    break;
            }
        }
        
        _logger.LogInformation("Streaming complete: yielded {Count} results for {Keyword}", yielded, parentKeyword);
    }

    /// <summary>
    /// Check if a keyword is related to the seed keyword
    /// Must contain the seed or share at least one significant word
    /// </summary>
    private static bool IsRelatedKeyword(string keyword, string seedKeyword, HashSet<string> seedWords)
    {
        var keywordLower = keyword.ToLowerInvariant();
        var seedLower = seedKeyword.ToLowerInvariant();
        
        // Option 1: Keyword contains the entire seed
        if (keywordLower.Contains(seedLower))
            return true;
        
        // Option 2: Seed contains the entire keyword (e.g., "kilimani" for "kilimani apartments")
        if (seedLower.Contains(keywordLower))
            return true;
        
        // Option 3: Keyword shares at least one significant word with seed
        var keywordWords = keywordLower
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet();
        
        return seedWords.Overlaps(keywordWords);
    }
}
