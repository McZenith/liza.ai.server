namespace Liza.Infrastructure.Analysis;

using System.Text.RegularExpressions;
using Liza.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for calculating final keyword opportunity and difficulty scores
/// </summary>
public interface IKeywordScoreService
{
    Task<KeywordScores> CalculateAsync(
        SearchDemand demand, 
        ContentSupply supply, 
        RankingInsights insights);
    
    Task<ContentRecommendations> GenerateRecommendationsAsync(
        string keyword,
        List<EnrichedVideo> videos,
        KeywordData? keywordData,
        TrendData? trendData);
}

public class KeywordScoreService : IKeywordScoreService
{
    private readonly ILogger<KeywordScoreService> _logger;

    public KeywordScoreService(ILogger<KeywordScoreService> logger)
    {
        _logger = logger;
    }

    public Task<KeywordScores> CalculateAsync(
        SearchDemand demand, 
        ContentSupply supply, 
        RankingInsights insights)
    {
        _logger.LogInformation("Calculating keyword scores");
        
        // Opportunity Score (0-100)
        // Higher = better opportunity
        var opportunityScore = CalculateOpportunityScore(demand, supply, insights);
        
        // Difficulty Score (0-100)
        // Higher = harder to rank
        var difficultyScore = CalculateDifficultyScore(supply, insights);
        
        // Letter grade
        var grade = CalculateGrade(opportunityScore, difficultyScore);
        
        return Task.FromResult(new KeywordScores
        {
            Opportunity = opportunityScore,
            Difficulty = difficultyScore,
            Grade = grade
        });
    }

    private int CalculateOpportunityScore(SearchDemand demand, ContentSupply supply, RankingInsights insights)
    {
        // Search Volume Factor (0-25 points)
        var volumeScore = demand.Volume switch
        {
            > 100000 => 25,
            > 50000 => 22,
            > 10000 => 18,
            > 1000 => 12,
            > 100 => 6,
            _ => 3
        };
        
        // Content Gap Factor (0-30 points)
        var gapScore = supply.ContentGapScore switch
        {
            > 1.5 => 30,  // Huge gap
            > 1.0 => 25,  // Good opportunity
            > 0.5 => 15,  // Moderate
            _ => 5        // Saturated
        };
        
        // Trend Momentum Factor (0-20 points)
        var momentumScore = demand.TrendType switch
        {
            TrendType.Trending => 20,
            TrendType.Consistent => 15,
            TrendType.Seasonal when IsInSeason(demand.SeasonalPeak) => 18,
            TrendType.Seasonal => 8,
            TrendType.Declining => 3,
            _ => 10
        };
        
        // Rankability Factor (0-25 points)
        // Based on whether you need a big channel to rank
        var rankabilityScore = insights.ChannelAuthority.NeedsEstablishedChannel ? 5 : 25;
        if (supply.CompetitionLevel == CompetitionLevel.Low) rankabilityScore += 5;
        rankabilityScore = Math.Min(rankabilityScore, 25);
        
        var total = volumeScore + gapScore + momentumScore + rankabilityScore;
        return Math.Min(100, Math.Max(0, total));
    }

    private int CalculateDifficultyScore(ContentSupply supply, RankingInsights insights)
    {
        // Competitor Authority (0-30 points)
        var authorityScore = supply.AvgCompetitorSubscribers switch
        {
            > 1000000 => 30,  // Million+ subs
            > 500000 => 25,
            > 100000 => 18,
            > 10000 => 10,
            _ => 5
        };
        
        // Content Saturation (0-25 points)
        var saturationScore = supply.VideoCount switch
        {
            > 100 => 25,
            > 50 => 20,
            > 20 => 12,
            > 10 => 6,
            _ => 3
        };
        
        // View Competition (0-25 points)
        var viewScore = supply.AvgCompetitorViews switch
        {
            > 1000000 => 25,
            > 500000 => 20,
            > 100000 => 15,
            > 10000 => 8,
            _ => 3
        };
        
        // Channel Requirement (0-20 points)
        var channelScore = insights.ChannelAuthority.NeedsEstablishedChannel ? 20 : 5;
        if (insights.ChannelAuthority.MinSubscribersToRank > 50000) channelScore += 5;
        channelScore = Math.Min(channelScore, 20);
        
        var total = authorityScore + saturationScore + viewScore + channelScore;
        return Math.Min(100, Math.Max(0, total));
    }

    private string CalculateGrade(int opportunity, int difficulty)
    {
        // High opportunity + low difficulty = A
        var netScore = opportunity - difficulty;
        
        return netScore switch
        {
            > 40 => "A",
            > 20 => "B",
            > 0 => "C",
            > -20 => "D",
            _ => "F"
        };
    }

    private bool IsInSeason(string? seasonalPeak)
    {
        if (seasonalPeak == null) return false;
        
        var currentMonth = DateTime.UtcNow.ToString("MMMM");
        return currentMonth.Equals(seasonalPeak, StringComparison.OrdinalIgnoreCase);
    }

    public Task<ContentRecommendations> GenerateRecommendationsAsync(
        string keyword,
        List<EnrichedVideo> videos,
        KeywordData? keywordData,
        TrendData? trendData)
    {
        _logger.LogInformation("Generating content recommendations for: {Keyword}", keyword);
        
        var topVideos = videos.Take(10).ToList();
        
        // Extract title patterns from top performers
        var titlePatterns = ExtractTitlePatterns(keyword, topVideos);
        
        // Find common tags
        var mustHaveTags = FindCommonTags(topVideos);
        
        // Calculate optimal video length
        var optimalLength = CalculateOptimalLength(topVideos);
        
        // Extract top questions from comments
        var topQuestions = ExtractTopQuestions(videos);
        
        // Related keywords from various sources
        var relatedKeywords = GatherRelatedKeywords(keywordData, trendData, videos);
        
        return Task.FromResult(new ContentRecommendations
        {
            TitlePatterns = titlePatterns,
            MustHaveTags = mustHaveTags,
            OptimalLengthSeconds = optimalLength,
            TopQuestions = topQuestions,
            RelatedKeywords = relatedKeywords
        });
    }

    private List<string> ExtractTitlePatterns(string keyword, List<EnrichedVideo> topVideos)
    {
        var patterns = new List<string>();
        var keywordLower = keyword.ToLowerInvariant();
        
        foreach (var video in topVideos.Take(5))
        {
            var title = video.Details.Title;
            
            // Extract pattern by replacing specific terms with placeholders
            var pattern = Regex.Replace(title, @"\d{4}", "[YEAR]");
            pattern = Regex.Replace(pattern, @"\d+", "[NUMBER]");
            
            if (pattern.Length < 80 && !patterns.Contains(pattern))
            {
                patterns.Add(pattern);
            }
        }
        
        return patterns.Take(3).ToList();
    }

    private List<string> FindCommonTags(List<EnrichedVideo> topVideos)
    {
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var video in topVideos)
        {
            foreach (var tag in video.Details.Tags)
            {
                var normalizedTag = tag.ToLowerInvariant().Trim();
                tagCounts[normalizedTag] = tagCounts.GetValueOrDefault(normalizedTag) + 1;
            }
        }
        
        // Tags that appear in 30%+ of top videos (more lenient for better recommendations)
        var threshold = Math.Max(1, topVideos.Count * 3 / 10); // At least 1 video
        return tagCounts
            .Where(kv => kv.Value >= threshold)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(10)
            .ToList();
    }

    private int CalculateOptimalLength(List<EnrichedVideo> topVideos)
    {
        if (!topVideos.Any()) return 600; // Default 10 minutes
        
        var lengths = topVideos
            .Select(v => (int)v.Details.Duration.TotalSeconds)
            .Where(s => s > 0)
            .ToList();
        
        if (!lengths.Any()) return 600;
        
        // Use median for optimal length (less affected by outliers)
        lengths.Sort();
        return lengths[lengths.Count / 2];
    }

    private List<string> ExtractTopQuestions(List<EnrichedVideo> videos)
    {
        var questions = new List<string>();
        
        foreach (var video in videos.Take(20))
        {
            var questionComments = video.Comments
                .Where(c => c.IsQuestion && c.LikeCount >= 2) // Lowered threshold from 5 to 2
                .OrderByDescending(c => c.LikeCount)
                .Take(3);
            
            foreach (var comment in questionComments)
            {
                // Clean HTML artifacts from comment text
                var cleanedText = CleanHtml(comment.Text);
                
                // Extract just the question part
                var questionMatch = Regex.Match(cleanedText, @"([^.!?]*\?)");
                if (questionMatch.Success)
                {
                    var question = questionMatch.Value.Trim();
                    if (question.Length > 15 && question.Length < 200 && !questions.Any(q => q.Contains(question)))
                    {
                        questions.Add(question);
                    }
                }
            }
        }
        
        return questions.Take(5).ToList();
    }

    /// <summary>
    /// Clean HTML tags and decode entities from text
    /// </summary>
    private string CleanHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        // Remove HTML tags
        text = Regex.Replace(text, @"<[^>]+>", " ");
        
        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);
        
        // Remove URLs
        text = Regex.Replace(text, @"https?://\S+|www\.\S+", "", RegexOptions.IgnoreCase);
        
        // Clean up whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();
        
        return text;
    }

    private List<string> GatherRelatedKeywords(
        KeywordData? keywordData, 
        TrendData? trendData, 
        List<EnrichedVideo> videos)
    {
        var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // PRIMARY: Extract from video tags (always available)
        var videoTags = videos
            .SelectMany(v => v.Details.Tags)
            .GroupBy(t => t.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key);
        
        foreach (var tag in videoTags)
            related.Add(tag);
        
        // SUPPLEMENT: Add from Google Ads if available
        if (keywordData?.RelatedQueries != null)
        {
            foreach (var q in keywordData.RelatedQueries.Take(5))
                related.Add(q);
        }
        
        // SUPPLEMENT: Add from Google Trends if available
        if (trendData?.RelatedQueries != null)
        {
            foreach (var q in trendData.RelatedQueries.Take(5))
                related.Add(q);
        }
        
        // SUPPLEMENT: Add rising queries if available
        if (trendData?.RisingQueries != null)
        {
            foreach (var q in trendData.RisingQueries.Take(5))
                related.Add(q);
        }
        
        return related.Take(10).ToList();
    }
}
