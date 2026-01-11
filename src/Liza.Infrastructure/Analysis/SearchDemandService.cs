namespace Liza.Infrastructure.Analysis;

using Liza.Core.Models;
using Liza.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for analyzing search demand patterns
/// Classifies keywords as Seasonal, Trending, Consistent, or Declining
/// </summary>
public interface ISearchDemandService
{
    Task<SearchDemand> AnalyzeAsync(string keyword, TrendData? trendData, KeywordData? keywordData);
}

public class SearchDemandService : ISearchDemandService
{
    private readonly ILogger<SearchDemandService> _logger;
    
    // Seasonal keywords and their peak months
    private static readonly Dictionary<string[], string> SeasonalPatterns = new()
    {
        { new[] { "christmas", "holiday", "gift" }, "December" },
        { new[] { "halloween", "costume", "scary" }, "October" },
        { new[] { "summer", "beach", "vacation" }, "July" },
        { new[] { "back to school", "school supplies" }, "August" },
        { new[] { "tax", "taxes", "tax return" }, "April" },
        { new[] { "valentine", "romantic" }, "February" },
        { new[] { "thanksgiving", "turkey" }, "November" },
        { new[] { "new year", "resolution" }, "January" }
    };

    public SearchDemandService(ILogger<SearchDemandService> logger)
    {
        _logger = logger;
    }

    public Task<SearchDemand> AnalyzeAsync(string keyword, TrendData? trendData, KeywordData? keywordData)
    {
        _logger.LogInformation("Analyzing search demand for: {Keyword}", keyword);
        
        var volume = keywordData?.SearchVolume ?? 0;
        var momentum = CalculateMomentum(trendData);
        var trendType = ClassifyTrendType(keyword, trendData, momentum);
        var seasonalPeak = DetectSeasonalPeak(keyword);
        
        return Task.FromResult(new SearchDemand
        {
            Volume = volume,
            TrendType = trendType,
            Momentum = momentum,
            SeasonalPeak = seasonalPeak
        });
    }

    private int CalculateMomentum(TrendData? trendData)
    {
        if (trendData == null) return 0;
        
        // Use direction from trends service
        return trendData.Direction switch
        {
            "rising" => trendData.InterestScore > 50 ? 50 : 25,
            "falling" => trendData.InterestScore > 50 ? -25 : -50,
            "stable" => 0,
            _ => 0
        };
    }

    private TrendType ClassifyTrendType(string keyword, TrendData? trendData, int momentum)
    {
        var lowerKeyword = keyword.ToLowerInvariant();
        
        // Check for seasonal patterns first
        foreach (var pattern in SeasonalPatterns)
        {
            if (pattern.Key.Any(p => lowerKeyword.Contains(p)))
            {
                return TrendType.Seasonal;
            }
        }
        
        // Classify based on momentum
        if (momentum > 30)
        {
            return TrendType.Trending;
        }
        else if (momentum < -30)
        {
            return TrendType.Declining;
        }
        
        // Check if rising queries include this keyword (trending indicator)
        if (trendData?.RisingQueries.Any(q => 
            q.Contains(keyword, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return TrendType.Trending;
        }
        
        return TrendType.Consistent;
    }

    private string? DetectSeasonalPeak(string keyword)
    {
        var lowerKeyword = keyword.ToLowerInvariant();
        
        foreach (var pattern in SeasonalPatterns)
        {
            if (pattern.Key.Any(p => lowerKeyword.Contains(p)))
            {
                return pattern.Value;
            }
        }
        
        return null;
    }
}
