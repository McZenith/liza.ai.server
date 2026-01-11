namespace Liza.Infrastructure.Reddit;

using System.Text.Json;
using System.Web;
using Liza.Core.Models;
using Liza.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reddit service using public JSON API (no auth required for read-only)
/// </summary>
public class RedditService : IRedditService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RedditService> _logger;

    public RedditService(HttpClient httpClient, ILogger<RedditService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Liza.ai/1.0 (Keyword Research Tool)");
    }

    public async Task<List<RedditPost>> SearchPostsAsync(string query, int limit = 25)
    {
        try
        {
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"https://www.reddit.com/search.json?q={encoded}&sort=relevance&limit={limit}";
            
            _logger.LogInformation("Searching Reddit for: {Query}", query);
            
            var response = await _httpClient.GetStringAsync(url);
            return ParseRedditResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search Reddit for: {Query}", query);
            return [];
        }
    }

    public async Task<List<RedditPost>> SearchSubredditAsync(string subreddit, string query, int limit = 25)
    {
        try
        {
            var encodedQuery = HttpUtility.UrlEncode(query);
            var url = $"https://www.reddit.com/r/{subreddit}/search.json?q={encodedQuery}&restrict_sr=1&sort=relevance&limit={limit}";
            
            _logger.LogInformation("Searching r/{Subreddit} for: {Query}", subreddit, query);
            
            var response = await _httpClient.GetStringAsync(url);
            return ParseRedditResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search r/{Subreddit} for: {Query}", subreddit, query);
            return [];
        }
    }

    private List<RedditPost> ParseRedditResponse(string json)
    {
        var posts = new List<RedditPost>();
        
        try
        {
            var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return posts;
            
            if (!data.TryGetProperty("children", out var children))
                return posts;
            
            foreach (var child in children.EnumerateArray())
            {
                if (!child.TryGetProperty("data", out var postData))
                    continue;
                
                var post = new RedditPost
                {
                    PostId = GetString(postData, "id"),
                    Title = GetString(postData, "title"),
                    SelfText = GetStringOrNull(postData, "selftext"),
                    Subreddit = GetString(postData, "subreddit"),
                    Score = GetInt(postData, "score"),
                    NumComments = GetInt(postData, "num_comments"),
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(
                        (long)GetDouble(postData, "created_utc")).UtcDateTime,
                    Url = GetString(postData, "url")
                };
                
                posts.Add(post);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Reddit response");
        }
        
        _logger.LogInformation("Found {Count} Reddit posts", posts.Count);
        return posts;
    }

    private static string GetString(JsonElement elem, string prop) =>
        elem.TryGetProperty(prop, out var val) ? val.GetString() ?? "" : "";
    
    private static string? GetStringOrNull(JsonElement elem, string prop) =>
        elem.TryGetProperty(prop, out var val) ? val.GetString() : null;
    
    private static int GetInt(JsonElement elem, string prop) =>
        elem.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number 
            ? val.GetInt32() : 0;
    
    private static double GetDouble(JsonElement elem, string prop) =>
        elem.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number 
            ? val.GetDouble() : 0;
}
