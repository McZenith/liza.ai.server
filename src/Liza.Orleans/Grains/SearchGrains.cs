namespace Liza.Orleans.Grains;

using Liza.Core.Models;
using Liza.Core.Services;
using Liza.Orleans.Grains.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Grain that searches YouTube for videos matching a keyword
/// </summary>
public class YouTubeSearchGrain : Grain, IYouTubeSearchGrain
{
    private readonly IYouTubeService _youtube;
    private readonly ILogger<YouTubeSearchGrain> _logger;
    
    // Grain state for caching within session
    private List<VideoData>? _cachedResults;
    private DateTime _cacheTime;

    public YouTubeSearchGrain(
        IYouTubeService youtube,
        ILogger<YouTubeSearchGrain> logger)
    {
        _youtube = youtube;
        _logger = logger;
    }

    public async Task<List<VideoData>> SearchAsync(int maxResults = 50)
    {
        var keyword = this.GetPrimaryKeyString();
        
        // Check grain-level cache (valid for 10 minutes)
        if (_cachedResults != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(10))
        {
            _logger.LogDebug("Returning cached search results for: {Keyword}", keyword);
            return _cachedResults;
        }
        
        _logger.LogInformation("Searching YouTube for: {Keyword}", keyword);
        
        try
        {
            _cachedResults = await _youtube.SearchVideosAsync(keyword, maxResults);
            _cacheTime = DateTime.UtcNow;
            
            _logger.LogInformation("Found {Count} videos for: {Keyword}", _cachedResults.Count, keyword);
            return _cachedResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search YouTube for: {Keyword}", keyword);
            return [];
        }
    }
}

/// <summary>
/// Grain that gets autocomplete suggestions from YouTube
/// </summary>
public class YouTubeAutocompleteGrain : Grain, IYouTubeAutocompleteGrain
{
    private readonly IAutocompleteService _autocomplete;
    private readonly ILogger<YouTubeAutocompleteGrain> _logger;
    
    private List<string>? _cachedSuggestions;
    private DateTime _cacheTime;

    public YouTubeAutocompleteGrain(
        IAutocompleteService autocomplete,
        ILogger<YouTubeAutocompleteGrain> logger)
    {
        _autocomplete = autocomplete;
        _logger = logger;
    }

    public async Task<List<string>> GetSuggestionsAsync()
    {
        var keyword = this.GetPrimaryKeyString();
        
        // Check grain-level cache (valid for 1 hour - autocomplete doesn't change often)
        if (_cachedSuggestions != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromHours(1))
        {
            return _cachedSuggestions;
        }
        
        _logger.LogDebug("Getting YouTube autocomplete for: {Keyword}", keyword);
        
        try
        {
            _cachedSuggestions = await _autocomplete.GetYouTubeSuggestionsAsync(keyword);
            _cacheTime = DateTime.UtcNow;
            return _cachedSuggestions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get YouTube autocomplete for: {Keyword}", keyword);
            return [];
        }
    }
}

/// <summary>
/// Grain that gets autocomplete suggestions from Google
/// </summary>
public class GoogleAutocompleteGrain : Grain, IGoogleAutocompleteGrain
{
    private readonly IAutocompleteService _autocomplete;
    private readonly ILogger<GoogleAutocompleteGrain> _logger;
    
    private List<string>? _cachedSuggestions;
    private DateTime _cacheTime;

    public GoogleAutocompleteGrain(
        IAutocompleteService autocomplete,
        ILogger<GoogleAutocompleteGrain> logger)
    {
        _autocomplete = autocomplete;
        _logger = logger;
    }

    public async Task<List<string>> GetSuggestionsAsync()
    {
        var keyword = this.GetPrimaryKeyString();
        
        if (_cachedSuggestions != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromHours(1))
        {
            return _cachedSuggestions;
        }
        
        _logger.LogDebug("Getting Google autocomplete for: {Keyword}", keyword);
        
        try
        {
            _cachedSuggestions = await _autocomplete.GetGoogleSuggestionsAsync(keyword);
            _cacheTime = DateTime.UtcNow;
            return _cachedSuggestions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Google autocomplete for: {Keyword}", keyword);
            return [];
        }
    }
}
