namespace Liza.Infrastructure.Search;

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Liza.Core.Models;
using Liza.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service interface for Google Video Search
/// </summary>
public interface IGoogleVideoSearchService
{
    Task<List<GoogleVideoResult>> SearchVideosAsync(string query, int limit = 10);
}

/// <summary>
/// Google Video Search using multiple approaches for reliability
/// Primary: YouTube Data API (if we have results from orchestrator)
/// Fallback: Google Video search scraping
/// </summary>
public class GoogleVideoSearchService : IGoogleVideoSearchService
{
    private readonly HttpClient _httpClient;
    private readonly ICacheService _cache;
    private readonly ILogger<GoogleVideoSearchService> _logger;
    private static readonly Random _random = new();
    
    // Rate limiting
    private static readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(2);

    // Multiple User-Agent strings to rotate
    private static readonly string[] UserAgents = 
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15"
    ];

    public GoogleVideoSearchService(HttpClient httpClient, ICacheService cache, ILogger<GoogleVideoSearchService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<GoogleVideoResult>> SearchVideosAsync(string query, int limit = 10)
    {
        // Check cache first
        var cacheKey = CacheKeys.GoogleVideo(query.ToLowerInvariant());
        var cached = await _cache.GetAsync<List<GoogleVideoResult>>(cacheKey);
        if (cached is { Count: > 0 })
        {
            _logger.LogDebug("Cache hit for Google Video search: {Query}", query);
            return cached.Take(limit).ToList();
        }
        
        // Try multiple approaches in order of reliability
        var results = await TryYouTubeInternalApiAsync(query, limit);
        
        if (results.Count == 0)
        {
            results = await TryGoogleVideoScrapingAsync(query, limit);
        }
        
        // Cache results if we got any
        if (results.Count > 0)
        {
            await _cache.SetAsync(cacheKey, results, CacheTtl.GoogleVideo);
        }
        
        return results;
    }

    /// <summary>
    /// Use YouTube's internal browse API (same as their website uses)
    /// More reliable than Google Video search scraping
    /// </summary>
    private async Task<List<GoogleVideoResult>> TryYouTubeInternalApiAsync(string query, int limit)
    {
        try
        {
            _logger.LogInformation("Trying YouTube internal search for: {Query}", query);
            
            // YouTube search page (simpler approach that still works)
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"https://www.youtube.com/results?search_query={encoded}";
            
            ConfigureHeaders();
            var html = await _httpClient.GetStringAsync(url);
            
            // Extract initial data JSON from the page
            var results = ParseYouTubeSearchResults(html, limit);
            
            if (results.Count > 0)
            {
                _logger.LogInformation("Found {Count} results from YouTube internal API", results.Count);
            }
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "YouTube internal API failed, falling back to Google Video search");
            return [];
        }
    }

    private List<GoogleVideoResult> ParseYouTubeSearchResults(string html, int limit)
    {
        var results = new List<GoogleVideoResult>();
        
        try
        {
            // YouTube embeds search results as JSON in the page
            // Look for ytInitialData
            var dataMatch = Regex.Match(html, @"var\s+ytInitialData\s*=\s*(\{.+?\});\s*</script>", RegexOptions.Singleline);
            
            if (!dataMatch.Success)
            {
                // Alternative pattern
                dataMatch = Regex.Match(html, @"ytInitialData\s*=\s*(\{.+?\});", RegexOptions.Singleline);
            }
            
            if (!dataMatch.Success)
            {
                _logger.LogDebug("Could not find ytInitialData in YouTube response");
                return results;
            }
            
            var jsonStr = dataMatch.Groups[1].Value;
            var doc = JsonDocument.Parse(jsonStr);
            
            // Navigate to video results
            // Path: contents.twoColumnSearchResultsRenderer.primaryContents.sectionListRenderer.contents[0].itemSectionRenderer.contents
            if (!doc.RootElement.TryGetProperty("contents", out var contents)) return results;
            if (!contents.TryGetProperty("twoColumnSearchResultsRenderer", out var twoCol)) return results;
            if (!twoCol.TryGetProperty("primaryContents", out var primary)) return results;
            if (!primary.TryGetProperty("sectionListRenderer", out var sectionList)) return results;
            if (!sectionList.TryGetProperty("contents", out var sections)) return results;
            
            foreach (var section in sections.EnumerateArray())
            {
                if (!section.TryGetProperty("itemSectionRenderer", out var itemSection)) continue;
                if (!itemSection.TryGetProperty("contents", out var items)) continue;
                
                foreach (var item in items.EnumerateArray())
                {
                    if (results.Count >= limit) break;
                    
                    if (item.TryGetProperty("videoRenderer", out var video))
                    {
                        var result = ParseVideoRenderer(video);
                        if (result != null) results.Add(result);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing YouTube search results JSON");
        }
        
        return results;
    }

    private GoogleVideoResult? ParseVideoRenderer(JsonElement video)
    {
        try
        {
            // Extract video ID
            var videoId = video.TryGetProperty("videoId", out var vid) ? vid.GetString() : null;
            if (string.IsNullOrEmpty(videoId)) return null;
            
            // Extract title
            string? title = null;
            if (video.TryGetProperty("title", out var titleObj) && 
                titleObj.TryGetProperty("runs", out var runs))
            {
                foreach (var run in runs.EnumerateArray())
                {
                    if (run.TryGetProperty("text", out var text))
                    {
                        title = text.GetString();
                        break;
                    }
                }
            }
            
            // Extract description snippet
            string? description = null;
            if (video.TryGetProperty("detailedMetadataSnippets", out var snippets))
            {
                foreach (var snippet in snippets.EnumerateArray())
                {
                    if (snippet.TryGetProperty("snippetText", out var snippetText) &&
                        snippetText.TryGetProperty("runs", out var snippetRuns))
                    {
                        var texts = new List<string>();
                        foreach (var run in snippetRuns.EnumerateArray())
                        {
                            if (run.TryGetProperty("text", out var t))
                            {
                                texts.Add(t.GetString() ?? "");
                            }
                        }
                        description = string.Join("", texts);
                        break;
                    }
                }
            }
            
            // Extract duration
            string? duration = null;
            if (video.TryGetProperty("lengthText", out var lengthText) &&
                lengthText.TryGetProperty("simpleText", out var durText))
            {
                duration = durText.GetString();
            }
            
            // Extract channel
            string? channel = null;
            if (video.TryGetProperty("ownerText", out var ownerText) &&
                ownerText.TryGetProperty("runs", out var ownerRuns))
            {
                foreach (var run in ownerRuns.EnumerateArray())
                {
                    if (run.TryGetProperty("text", out var text))
                    {
                        channel = text.GetString();
                        break;
                    }
                }
            }
            
            // Extract view count
            string? viewCount = null;
            if (video.TryGetProperty("viewCountText", out var viewText) &&
                viewText.TryGetProperty("simpleText", out var viewSimple))
            {
                viewCount = viewSimple.GetString();
            }
            
            // Extract thumbnail
            string? thumbnail = null;
            if (video.TryGetProperty("thumbnail", out var thumbObj) &&
                thumbObj.TryGetProperty("thumbnails", out var thumbs))
            {
                foreach (var thumb in thumbs.EnumerateArray())
                {
                    if (thumb.TryGetProperty("url", out var thumbUrl))
                    {
                        thumbnail = thumbUrl.GetString();
                        break;
                    }
                }
            }
            
            return new GoogleVideoResult
            {
                Title = title ?? "Unknown",
                Url = $"https://www.youtube.com/watch?v={videoId}",
                Description = description,
                Source = "YouTube",
                Duration = duration,
                Channel = channel,
                ViewCount = viewCount,
                ThumbnailUrl = thumbnail
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fallback: Scrape Google Video search results
    /// </summary>
    private async Task<List<GoogleVideoResult>> TryGoogleVideoScrapingAsync(string query, int limit)
    {
        try
        {
            _logger.LogInformation("Trying Google Video search for: {Query}", query);
            
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"https://www.google.com/search?q={encoded}&tbm=vid&num={limit}";
            
            ConfigureHeaders();
            
            // Add small delay to be polite
            await Task.Delay(_random.Next(100, 500));
            
            var html = await _httpClient.GetStringAsync(url);
            
            return ParseGoogleVideoResults(html, limit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search Google Videos for: {Query}", query);
            return [];
        }
    }

    private List<GoogleVideoResult> ParseGoogleVideoResults(string html, int limit)
    {
        var results = new List<GoogleVideoResult>();
        
        try
        {
            // Look for video cards - Google uses various patterns
            // Pattern 1: YouTube links
            var youtubePattern = new Regex(
                @"<a[^>]*href=""([^""]*(?:youtube\.com/watch\?v=|youtu\.be/)([^""\&]+)[^""]*)""[^>]*>.*?<h3[^>]*>([^<]+)</h3>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            foreach (Match match in youtubePattern.Matches(html))
            {
                if (results.Count >= limit) break;
                
                var url = match.Groups[1].Value;
                var title = HttpUtility.HtmlDecode(match.Groups[3].Value.Trim());
                
                // Clean up Google redirect URLs
                if (url.StartsWith("/url?"))
                {
                    var qMatch = Regex.Match(url, @"[?&]q=([^&]+)");
                    if (qMatch.Success)
                    {
                        url = HttpUtility.UrlDecode(qMatch.Groups[1].Value);
                    }
                }
                
                if (!string.IsNullOrEmpty(title) && !results.Any(r => r.Url == url))
                {
                    results.Add(new GoogleVideoResult
                    {
                        Title = title,
                        Url = url,
                        Source = ExtractSource(url),
                        Description = null
                    });
                }
            }
            
            // Pattern 2: General video links with h3 titles
            var generalPattern = new Regex(
                @"<a[^>]*href=""(/url\?[^""]*)""[^>]*>.*?<h3[^>]*>([^<]+)</h3>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            foreach (Match match in generalPattern.Matches(html))
            {
                if (results.Count >= limit) break;
                
                var rawUrl = match.Groups[1].Value;
                var title = HttpUtility.HtmlDecode(match.Groups[2].Value.Trim());
                
                // Extract actual URL from Google redirect
                var qMatch = Regex.Match(rawUrl, @"[?&]q=([^&]+)");
                if (!qMatch.Success) continue;
                
                var url = HttpUtility.UrlDecode(qMatch.Groups[1].Value);
                
                if (!IsVideoUrl(url)) continue;
                if (results.Any(r => r.Url == url)) continue;
                
                results.Add(new GoogleVideoResult
                {
                    Title = title,
                    Url = url,
                    Source = ExtractSource(url),
                    Description = null
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing Google Video results");
        }
        
        _logger.LogInformation("Found {Count} Google Video results", results.Count);
        return results;
    }

    private void ConfigureHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgents[_random.Next(UserAgents.Length)]);
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
    }

    private static bool IsVideoUrl(string url) =>
        url.Contains("youtube.com") || 
        url.Contains("youtu.be") || 
        url.Contains("vimeo.com") ||
        url.Contains("dailymotion.com") ||
        url.Contains("tiktok.com") ||
        url.Contains("twitch.tv");

    private static string ExtractSource(string url)
    {
        if (url.Contains("youtube.com") || url.Contains("youtu.be")) return "YouTube";
        if (url.Contains("vimeo.com")) return "Vimeo";
        if (url.Contains("dailymotion.com")) return "Dailymotion";
        if (url.Contains("tiktok.com")) return "TikTok";
        if (url.Contains("twitch.tv")) return "Twitch";
        return "Unknown";
    }
}
