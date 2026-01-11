namespace Liza.Orleans.Grains;

using Liza.Core.Models;
using Liza.Orleans.Grains.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Main orchestrator grain that coordinates all extraction sources in parallel
/// This is the entry point for keyword research
/// </summary>
public class ResearchOrchestratorGrain : Grain, IResearchOrchestratorGrain
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<ResearchOrchestratorGrain> _logger;
    
    private KeywordResearchResult? _cachedResult;

    public ResearchOrchestratorGrain(
        IGrainFactory grainFactory,
        ILogger<ResearchOrchestratorGrain> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task<KeywordResearchResult> ExecuteAsync()
    {
        var keyword = this.GetPrimaryKeyString();
        
        // Return cached if still fresh (research valid for 1 hour)
        if (_cachedResult != null && 
            DateTime.UtcNow - _cachedResult.ResearchedAt < TimeSpan.FromHours(1))
        {
            _logger.LogDebug("Returning cached research for: {Keyword}", keyword);
            return _cachedResult;
        }
        
        _logger.LogInformation("Starting keyword research for: {Keyword}", keyword);
        var startTime = DateTime.UtcNow;
        
        // PHASE 1: Launch all initial extractions in parallel
        var searchTask = _grainFactory
            .GetGrain<IYouTubeSearchGrain>(keyword)
            .SearchAsync(50);
        
        var ytAutocompleteTask = _grainFactory
            .GetGrain<IYouTubeAutocompleteGrain>(keyword)
            .GetSuggestionsAsync();
        
        var googleAutocompleteTask = _grainFactory
            .GetGrain<IGoogleAutocompleteGrain>(keyword)
            .GetSuggestionsAsync();
        
        var keywordPlannerTask = _grainFactory
            .GetGrain<IKeywordPlannerGrain>(keyword)
            .GetMetricsAsync();
        
        // TODO: Add these when implemented
        // var trendsTask = _grainFactory.GetGrain<IGoogleTrendsGrain>(keyword).GetTrendsAsync();
        // var redditTask = _grainFactory.GetGrain<IRedditGrain>(keyword).SearchAsync();
        
        // Wait for phase 1
        await Task.WhenAll(searchTask, ytAutocompleteTask, googleAutocompleteTask, keywordPlannerTask);
        
        var searchResults = await searchTask;
        var ytSuggestions = await ytAutocompleteTask;
        var googleSuggestions = await googleAutocompleteTask;
        var keywordData = await keywordPlannerTask;
        
        _logger.LogInformation(
            "Phase 1 complete for {Keyword}: {VideoCount} videos, {YTSugg} YT suggestions, {GSugg} Google suggestions, Volume={Volume}",
            keyword, searchResults.Count, ytSuggestions.Count, googleSuggestions.Count, keywordData?.SearchVolume ?? 0);
        
        // PHASE 2: Fast enrich top videos (skip transcripts for speed)
        var enrichmentTasks = searchResults
            .Take(10) // Enrich top 10 with fast method (~500ms each)
            .Select(v => EnrichVideoFast(v.VideoId))
            .ToList();
        
        var enrichedVideos = await Task.WhenAll(enrichmentTasks);
        
        var elapsed = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "Research complete for {Keyword} in {Elapsed}ms: {EnrichedCount} enriched videos (of {Total} total)",
            keyword, elapsed.TotalMilliseconds, enrichedVideos.Count(v => v != null), searchResults.Count);
        
        _cachedResult = new KeywordResearchResult
        {
            Keyword = keyword,
            ResearchedAt = DateTime.UtcNow,
            Videos = enrichedVideos.Where(v => v != null).ToList()!,
            YouTubeSuggestions = ytSuggestions,
            GoogleSuggestions = googleSuggestions,
            KeywordMetrics = keywordData,
            TotalVideoCount = searchResults.Count,  // Track total search results
            // TODO: Add when implemented
            // RedditPosts = redditPosts
        };
        
        return _cachedResult;
    }

    public async IAsyncEnumerable<PartialResearchResult> StreamAsync()
    {
        var keyword = this.GetPrimaryKeyString();
        
        _logger.LogInformation("Streaming research for: {Keyword}", keyword);
        
        // Launch all tasks
        var searchTask = _grainFactory
            .GetGrain<IYouTubeSearchGrain>(keyword)
            .SearchAsync(50);
        
        var ytAutocompleteTask = _grainFactory
            .GetGrain<IYouTubeAutocompleteGrain>(keyword)
            .GetSuggestionsAsync();
        
        var googleAutocompleteTask = _grainFactory
            .GetGrain<IGoogleAutocompleteGrain>(keyword)
            .GetSuggestionsAsync();
        
        // Create task list with source names
        var tasks = new List<(Task Task, string Source)>
        {
            (searchTask, "youtube_search"),
            (ytAutocompleteTask, "youtube_autocomplete"),
            (googleAutocompleteTask, "google_autocomplete")
        };
        
        // Yield results as they complete
        while (tasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(tasks.Select(t => t.Task));
            var taskInfo = tasks.First(t => t.Task == completedTask);
            tasks.RemoveAll(t => t.Task == completedTask);
            
            object data = taskInfo.Source switch
            {
                "youtube_search" => await searchTask,
                "youtube_autocomplete" => await ytAutocompleteTask,
                "google_autocomplete" => await googleAutocompleteTask,
                _ => new object()
            };
            
            yield return new PartialResearchResult
            {
                Source = taskInfo.Source,
                Data = data,
                CompletedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<EnrichedVideo?> EnrichVideoSafe(string videoId)
    {
        try
        {
            return await _grainFactory
                .GetGrain<IVideoEnrichmentGrain>(videoId)
                .EnrichAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich video: {VideoId}", videoId);
            return null;
        }
    }

    /// <summary>
    /// Fast enrichment without transcript for speed (~500ms vs ~2-3s)
    /// </summary>
    private async Task<EnrichedVideo?> EnrichVideoFast(string videoId)
    {
        try
        {
            return await _grainFactory
                .GetGrain<IVideoEnrichmentGrain>(videoId)
                .EnrichFastAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fast-enrich video: {VideoId}", videoId);
            return null;
        }
    }
}
