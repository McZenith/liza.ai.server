namespace Liza.Infrastructure.Trends;

using System.Text.Json;
using System.Web;
using Liza.Core.Models;
using Liza.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Google Trends service using unofficial API endpoints (free)
/// Uses the same internal APIs that the Google Trends website uses
/// </summary>
public class GoogleTrendsService : IGoogleTrendsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleTrendsService> _logger;
    private const string BaseUrl = "https://trends.google.com/trends/api";

    public GoogleTrendsService(HttpClient httpClient, ILogger<GoogleTrendsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Set required headers to mimic browser
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    }

    public async Task<TrendData> GetTrendsAsync(string keyword, string region = "US")
    {
        try
        {
            _logger.LogInformation("Getting trends for: {Keyword} in {Region}", keyword, region);
            
            // Step 1: Get widget tokens from explore endpoint
            var widgetData = await GetWidgetTokensAsync(keyword, region);
            
            if (widgetData == null)
            {
                return CreateEmptyTrendData(keyword);
            }
            
            // Step 2: Get interest over time data
            var interestScore = await GetInterestScoreAsync(widgetData.TimelineToken, widgetData.TimelineRequest);
            var direction = await CalculateTrendDirectionAsync(widgetData.TimelineToken, widgetData.TimelineRequest);
            
            // Step 3: Get related and rising queries
            var relatedQueries = await GetRelatedQueriesInternalAsync(widgetData.RelatedToken, widgetData.RelatedRequest);
            var risingQueries = await GetRisingQueriesInternalAsync(widgetData.RelatedToken, widgetData.RelatedRequest);
            
            return new TrendData
            {
                Keyword = keyword,
                InterestScore = interestScore,
                Direction = direction,
                RelatedQueries = relatedQueries,
                RisingQueries = risingQueries
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get trends for: {Keyword}", keyword);
            return CreateEmptyTrendData(keyword);
        }
    }

    public async Task<List<string>> GetRelatedQueriesAsync(string keyword)
    {
        try
        {
            var widgetData = await GetWidgetTokensAsync(keyword, "US");
            if (widgetData == null) return [];
            
            return await GetRelatedQueriesInternalAsync(widgetData.RelatedToken, widgetData.RelatedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get related queries for: {Keyword}", keyword);
            return [];
        }
    }

    public async Task<List<string>> GetRisingQueriesAsync(string keyword)
    {
        try
        {
            var widgetData = await GetWidgetTokensAsync(keyword, "US");
            if (widgetData == null) return [];
            
            return await GetRisingQueriesInternalAsync(widgetData.RelatedToken, widgetData.RelatedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get rising queries for: {Keyword}", keyword);
            return [];
        }
    }

    public async Task<TrendData> GetYouTubeTrendsAsync(string keyword, string region = "US")
    {
        try
        {
            _logger.LogInformation("Getting YouTube trends for: {Keyword} in {Region}", keyword, region);
            
            // Use "youtube" as the property for YouTube-specific trends
            var widgetData = await GetWidgetTokensAsync(keyword, region, "youtube");
            
            if (widgetData == null)
            {
                return CreateEmptyTrendData(keyword);
            }
            
            var interestScore = await GetInterestScoreAsync(widgetData.TimelineToken, widgetData.TimelineRequest);
            var direction = await CalculateTrendDirectionAsync(widgetData.TimelineToken, widgetData.TimelineRequest);
            var relatedQueries = await GetRelatedQueriesInternalAsync(widgetData.RelatedToken, widgetData.RelatedRequest);
            var risingQueries = await GetRisingQueriesInternalAsync(widgetData.RelatedToken, widgetData.RelatedRequest);
            
            return new TrendData
            {
                Keyword = keyword,
                InterestScore = interestScore,
                Direction = direction,
                RelatedQueries = relatedQueries,
                RisingQueries = risingQueries
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get YouTube trends for: {Keyword}", keyword);
            return CreateEmptyTrendData(keyword);
        }
    }

    public async Task<List<string>> GetYouTubeRisingQueriesAsync(string keyword)
    {
        try
        {
            var widgetData = await GetWidgetTokensAsync(keyword, "US", "youtube");
            if (widgetData == null) return [];
            
            return await GetRisingQueriesInternalAsync(widgetData.RelatedToken, widgetData.RelatedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get YouTube rising queries for: {Keyword}", keyword);
            return [];
        }
    }

    private async Task<WidgetTokens?> GetWidgetTokensAsync(string keyword, string region, string property = "")
    {
        try
        {
            var requestPayload = new
            {
                comparisonItem = new[]
                {
                    new { keyword, geo = region, time = "today 3-m" }
                },
                category = 0,
                property  // "youtube" for YouTube Search, "" for Web Search
            };
            
            var reqJson = JsonSerializer.Serialize(requestPayload);
            var url = $"{BaseUrl}/explore?hl=en-US&tz=240&req={HttpUtility.UrlEncode(reqJson)}";
            
            var response = await _httpClient.GetStringAsync(url);
            
            // Response starts with ")]}'\" which we need to skip
            var json = CleanTrendsResponse(response);
            if (json == null) return null;
            
            var doc = JsonDocument.Parse(json);
            var widgets = doc.RootElement.GetProperty("widgets");
            
            string? timelineToken = null;
            string? timelineRequest = null;
            string? relatedToken = null;
            string? relatedRequest = null;
            
            foreach (var widget in widgets.EnumerateArray())
            {
                var id = widget.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                
                if (id == "TIMESERIES")
                {
                    timelineToken = widget.GetProperty("token").GetString();
                    timelineRequest = widget.GetProperty("request").ToString();
                }
                else if (id == "RELATED_QUERIES")
                {
                    relatedToken = widget.GetProperty("token").GetString();
                    relatedRequest = widget.GetProperty("request").ToString();
                }
            }
            
            if (timelineToken == null || relatedToken == null) return null;
            
            return new WidgetTokens
            {
                TimelineToken = timelineToken,
                TimelineRequest = timelineRequest ?? "{}",
                RelatedToken = relatedToken,
                RelatedRequest = relatedRequest ?? "{}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get widget tokens for: {Keyword}", keyword);
            return null;
        }
    }

    private async Task<int> GetInterestScoreAsync(string token, string request)
    {
        try
        {
            var url = $"{BaseUrl}/widgetdata/multiline?hl=en-US&tz=240&req={HttpUtility.UrlEncode(request)}&token={token}";
            var response = await _httpClient.GetStringAsync(url);
            
            var json = CleanTrendsResponse(response);
            if (json == null) return 0;
            
            var doc = JsonDocument.Parse(json);
            
            // Navigate to timeline data
            if (!doc.RootElement.TryGetProperty("default", out var defaultProp)) return 0;
            if (!defaultProp.TryGetProperty("timelineData", out var timeline)) return 0;
            
            // Calculate average interest score from the timeline
            var scores = new List<int>();
            foreach (var point in timeline.EnumerateArray())
            {
                if (point.TryGetProperty("value", out var values))
                {
                    foreach (var val in values.EnumerateArray())
                    {
                        scores.Add(val.GetInt32());
                    }
                }
            }
            
            if (scores.Count == 0) return 0;
            
            // Return the average score (represents overall interest level)
            return (int)scores.Average();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get interest score");
            return 0;
        }
    }

    private async Task<string> CalculateTrendDirectionAsync(string token, string request)
    {
        try
        {
            var url = $"{BaseUrl}/widgetdata/multiline?hl=en-US&tz=240&req={HttpUtility.UrlEncode(request)}&token={token}";
            var response = await _httpClient.GetStringAsync(url);
            
            var json = CleanTrendsResponse(response);
            if (json == null) return "unknown";
            
            var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("default", out var defaultProp)) return "unknown";
            if (!defaultProp.TryGetProperty("timelineData", out var timeline)) return "unknown";
            
            var scores = new List<int>();
            foreach (var point in timeline.EnumerateArray())
            {
                if (point.TryGetProperty("value", out var values))
                {
                    foreach (var val in values.EnumerateArray())
                    {
                        scores.Add(val.GetInt32());
                        break; // Just first value per point
                    }
                }
            }
            
            if (scores.Count < 4) return "unknown";
            
            // Compare last quarter vs first quarter
            var firstQuarter = scores.Take(scores.Count / 4).Average();
            var lastQuarter = scores.Skip(3 * scores.Count / 4).Average();
            
            var change = (lastQuarter - firstQuarter) / Math.Max(firstQuarter, 1) * 100;
            
            if (change > 20) return "rising";
            if (change < -20) return "falling";
            return "stable";
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task<List<string>> GetRelatedQueriesInternalAsync(string token, string request)
    {
        try
        {
            var url = $"{BaseUrl}/widgetdata/relatedsearches?hl=en-US&tz=240&req={HttpUtility.UrlEncode(request)}&token={token}";
            var response = await _httpClient.GetStringAsync(url);
            
            var json = CleanTrendsResponse(response);
            if (json == null) return [];
            
            var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("default", out var defaultProp)) return [];
            if (!defaultProp.TryGetProperty("rankedList", out var rankedList)) return [];
            
            var queries = new List<string>();
            
            foreach (var list in rankedList.EnumerateArray())
            {
                // First list is "Top" queries
                if (list.TryGetProperty("rankedKeyword", out var keywords))
                {
                    foreach (var kw in keywords.EnumerateArray())
                    {
                        if (kw.TryGetProperty("query", out var query))
                        {
                            var q = query.GetString();
                            if (!string.IsNullOrEmpty(q)) queries.Add(q);
                        }
                    }
                    break; // Just get the first list (Top queries)
                }
            }
            
            return queries.Take(10).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get related queries");
            return [];
        }
    }

    private async Task<List<string>> GetRisingQueriesInternalAsync(string token, string request)
    {
        try
        {
            var url = $"{BaseUrl}/widgetdata/relatedsearches?hl=en-US&tz=240&req={HttpUtility.UrlEncode(request)}&token={token}";
            var response = await _httpClient.GetStringAsync(url);
            
            var json = CleanTrendsResponse(response);
            if (json == null) return [];
            
            var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("default", out var defaultProp)) return [];
            if (!defaultProp.TryGetProperty("rankedList", out var rankedList)) return [];
            
            var queries = new List<string>();
            var listIndex = 0;
            
            foreach (var list in rankedList.EnumerateArray())
            {
                // Second list is "Rising" queries
                if (listIndex == 1 && list.TryGetProperty("rankedKeyword", out var keywords))
                {
                    foreach (var kw in keywords.EnumerateArray())
                    {
                        if (kw.TryGetProperty("query", out var query))
                        {
                            var q = query.GetString();
                            if (!string.IsNullOrEmpty(q)) queries.Add(q);
                        }
                    }
                    break;
                }
                listIndex++;
            }
            
            return queries.Take(10).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get rising queries");
            return [];
        }
    }

    private static string? CleanTrendsResponse(string response)
    {
        // Google Trends responses start with ")]}'" which we need to skip
        var jsonStart = response.IndexOf('{');
        if (jsonStart < 0) return null;
        return response[jsonStart..];
    }

    private static TrendData CreateEmptyTrendData(string keyword) => new()
    {
        Keyword = keyword,
        InterestScore = 0,
        Direction = "unknown",
        RelatedQueries = [],
        RisingQueries = []
    };

    private class WidgetTokens
    {
        public required string TimelineToken { get; init; }
        public required string TimelineRequest { get; init; }
        public required string RelatedToken { get; init; }
        public required string RelatedRequest { get; init; }
    }
}
