namespace Liza.Infrastructure.Autocomplete;

using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Liza.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Scrapes autocomplete suggestions from YouTube and Google (free, no API cost)
/// </summary>
public class AutocompleteService : IAutocompleteService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AutocompleteService> _logger;

    public AutocompleteService(HttpClient httpClient, ILogger<AutocompleteService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<string>> GetYouTubeSuggestionsAsync(string query)
    {
        try
        {
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"https://suggestqueries-clients6.youtube.com/complete/search?client=youtube&q={encoded}&ds=yt";
            
            var response = await _httpClient.GetStringAsync(url);
            
            // Response is JSONP: window.google.ac.h([...])
            // Extract the JSON array
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']') + 1;
            
            if (jsonStart < 0 || jsonEnd <= jsonStart)
                return [];
            
            var json = response[jsonStart..jsonEnd];
            var parsed = JsonDocument.Parse(json);
            
            var suggestions = new List<string>();
            
            // Structure: [query, [[suggestion1, ...], [suggestion2, ...], ...]]
            if (parsed.RootElement.GetArrayLength() >= 2)
            {
                var suggestionsArray = parsed.RootElement[1];
                foreach (var item in suggestionsArray.EnumerateArray())
                {
                    if (item.GetArrayLength() > 0)
                    {
                        var suggestion = item[0].GetString();
                        if (!string.IsNullOrEmpty(suggestion))
                            suggestions.Add(suggestion);
                    }
                }
            }
            
            _logger.LogDebug("Got {Count} YouTube suggestions for '{Query}'", suggestions.Count, query);
            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get YouTube autocomplete for '{Query}'", query);
            return [];
        }
    }

    public async Task<List<string>> GetGoogleSuggestionsAsync(string query)
    {
        try
        {
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"https://suggestqueries.google.com/complete/search?client=firefox&q={encoded}";
            
            var response = await _httpClient.GetStringAsync(url);
            var parsed = JsonDocument.Parse(response);
            
            var suggestions = new List<string>();
            
            // Structure: [query, [suggestion1, suggestion2, ...]]
            if (parsed.RootElement.GetArrayLength() >= 2)
            {
                foreach (var item in parsed.RootElement[1].EnumerateArray())
                {
                    var suggestion = item.GetString();
                    if (!string.IsNullOrEmpty(suggestion))
                        suggestions.Add(suggestion);
                }
            }
            
            _logger.LogDebug("Got {Count} Google suggestions for '{Query}'", suggestions.Count, query);
            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Google autocomplete for '{Query}'", query);
            return [];
        }
    }
}

/// <summary>
/// Generate "Answer The Public" style question patterns
/// </summary>
public static class QuestionGenerator
{
    private static readonly string[] QuestionWords = ["what", "how", "why", "when", "where", "who", "which", "can", "does", "is", "are", "will", "should"];
    private static readonly string[] Prepositions = ["for", "with", "without", "vs", "versus", "or", "and", "to", "in", "on", "near", "like"];
    private static readonly string[] Comparisons = ["best", "top", "vs", "alternative", "review", "tutorial", "guide", "example", "course", "free"];

    /// <summary>
    /// Generate question-based keyword variations
    /// </summary>
    public static List<string> GenerateQuestions(string keyword)
    {
        var questions = new List<string>();
        
        foreach (var word in QuestionWords)
        {
            questions.Add($"{word} {keyword}");
            questions.Add($"{word} is {keyword}");
            questions.Add($"{word} to {keyword}");
        }
        
        return questions;
    }

    /// <summary>
    /// Generate preposition-based variations
    /// </summary>
    public static List<string> GeneratePrepositions(string keyword)
    {
        var variations = new List<string>();
        
        foreach (var prep in Prepositions)
        {
            variations.Add($"{keyword} {prep}");
        }
        
        return variations;
    }

    /// <summary>
    /// Generate comparison/modifier variations
    /// </summary>
    public static List<string> GenerateComparisons(string keyword)
    {
        var variations = new List<string>();
        
        foreach (var comp in Comparisons)
        {
            variations.Add($"{comp} {keyword}");
            variations.Add($"{keyword} {comp}");
        }
        
        return variations;
    }

    /// <summary>
    /// Generate all keyword variations
    /// </summary>
    public static List<string> GenerateAllVariations(string keyword)
    {
        var all = new List<string>();
        all.AddRange(GenerateQuestions(keyword));
        all.AddRange(GeneratePrepositions(keyword));
        all.AddRange(GenerateComparisons(keyword));
        return all.Distinct().ToList();
    }
}
