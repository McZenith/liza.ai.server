namespace Liza.Infrastructure.Analysis;

using Liza.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for analyzing content supply and detecting content gaps
/// Content Gap = High demand but low supply (opportunity)
/// </summary>
public interface IContentGapService
{
    Task<ContentSupply> AnalyzeAsync(string keyword, List<EnrichedVideo> videos, KeywordData? keywordData, int totalSearchResults = 0);
}

public class ContentGapService : IContentGapService
{
    private readonly ILogger<ContentGapService> _logger;

    public ContentGapService(ILogger<ContentGapService> logger)
    {
        _logger = logger;
    }

    public Task<ContentSupply> AnalyzeAsync(string keyword, List<EnrichedVideo> videos, KeywordData? keywordData, int totalSearchResults = 0)
    {
        _logger.LogInformation("Analyzing content supply for: {Keyword} ({VideoCount} videos)", keyword, videos.Count);
        
        var videoCount = videos.Count;
        var searchVolume = keywordData?.SearchVolume ?? 0;
        var now = DateTime.UtcNow;
        
        // Calculate average competitor metrics
        var avgViews = videos.Any() 
            ? (long)videos.Average(v => v.Details.ViewCount) 
            : 0;
        
        var avgSubscribers = videos
            .Where(v => v.Channel != null)
            .Select(v => v.Channel!.SubscriberCount)
            .DefaultIfEmpty(0)
            .Average();
        
        // Calculate video velocity metrics (upload frequency by time period)
        var videosToday = videos.Count(v => v.Details.PublishedAt >= now.Date);
        var videosLast3Days = videos.Count(v => v.Details.PublishedAt >= now.AddDays(-3));
        var videosThisWeek = videos.Count(v => v.Details.PublishedAt >= now.AddDays(-7));
        var videosThisMonth = videos.Count(v => v.Details.PublishedAt >= now.AddDays(-30));
        var videosThisYear = videos.Count(v => v.Details.PublishedAt >= now.AddDays(-365));
        
        _logger.LogInformation(
            "Content velocity for {Keyword}: Today={Today}, 3Days={Days3}, Week={Week}, Month={Month}, Year={Year}. Sample dates: {Dates}",
            keyword, videosToday, videosLast3Days, videosThisWeek, videosThisMonth, videosThisYear,
            string.Join(", ", videos.Take(3).Select(v => v.Details.PublishedAt.ToString("yyyy-MM-dd"))));
        
        // Determine content activity level
        var activityLevel = DetermineContentActivityLevel(videosThisWeek, videosThisMonth, videoCount);
        
        // Calculate content gap score
        var contentGapScore = CalculateContentGapScore(searchVolume, videoCount, avgViews, (long)avgSubscribers);
        
        // Determine competition level
        var competitionLevel = DetermineCompetitionLevel(contentGapScore, avgViews, (long)avgSubscribers);
        
        // Dormant opportunity: high search volume but low recent activity
        var isDormant = IsDormantOpportunity(searchVolume, videosThisMonth, videosThisWeek, videoCount);
        
        return Task.FromResult(new ContentSupply
        {
            VideoCount = videoCount,
            TotalSearchResults = totalSearchResults > 0 ? totalSearchResults : videoCount,
            ContentGapScore = contentGapScore,
            CompetitionLevel = competitionLevel,
            AvgCompetitorViews = avgViews,
            AvgCompetitorSubscribers = (long)avgSubscribers,
            VideosUploadedToday = videosToday,
            VideosLast3Days = videosLast3Days,
            VideosThisWeek = videosThisWeek,
            VideosThisMonth = videosThisMonth,
            VideosThisYear = videosThisYear,
            IsDormantOpportunity = isDormant,
            ContentActivityLevel = activityLevel
        });
    }

    /// <summary>
    /// Determine how actively content is being created for this keyword
    /// </summary>
    private string DetermineContentActivityLevel(int videosThisWeek, int videosThisMonth, int totalVideos)
    {
        // Hot: 3+ videos per week
        if (videosThisWeek >= 3)
            return "Hot";
        
        // Active: 1-2 videos per week
        if (videosThisWeek >= 1)
            return "Active";
        
        // Moderate: at least 1 video per month
        if (videosThisMonth >= 1)
            return "Moderate";
        
        // Slow: has videos but nothing recent
        if (totalVideos > 0)
            return "Slow";
        
        // Dormant: no videos at all
        return "Dormant";
    }

    /// <summary>
    /// Identify dormant opportunities: high search volume but creators aren't serving it
    /// </summary>
    private bool IsDormantOpportunity(int searchVolume, int videosThisMonth, int videosThisWeek, int totalVideos)
    {
        // Need at least some search volume to be an opportunity
        if (searchVolume < 1000)
            return false;
        
        // Perfect opportunity: high demand, no recent supply
        if (searchVolume >= 5000 && videosThisMonth == 0)
            return true;
        
        // Good opportunity: decent demand, very little recent supply
        if (searchVolume >= 2000 && videosThisWeek == 0 && videosThisMonth <= 2)
            return true;
        
        // Moderate opportunity: some demand, slow supply
        if (searchVolume >= 1000 && videosThisWeek == 0 && totalVideos > 0)
            return true;
        
        return false;
    }

    private double CalculateContentGapScore(int searchVolume, int videoCount, long avgViews, long avgSubscribers)
    {
        if (videoCount == 0 && searchVolume > 0)
        {
            // No videos but search demand = huge opportunity
            return 2.0;
        }
        
        if (searchVolume == 0)
        {
            return 0.0;
        }
        
        // Normalize factors
        var volumeFactor = Math.Min(searchVolume / 10000.0, 1.0);  // Cap at 10k
        var supplyFactor = Math.Min(videoCount / 50.0, 1.0);       // Cap at 50 videos
        var authorityFactor = Math.Min(avgSubscribers / 1000000.0, 1.0);  // Cap at 1M subs
        
        // Content gap = demand / (supply * authority)
        var denominator = (supplyFactor + 0.1) * (authorityFactor + 0.1);
        var gap = volumeFactor / denominator;
        
        // Clamp to reasonable range
        return Math.Round(Math.Min(Math.Max(gap, 0.0), 2.0), 2);
    }

    private CompetitionLevel DetermineCompetitionLevel(double contentGapScore, long avgViews, long avgSubscribers)
    {
        // Content gap > 1 means opportunity (low competition)
        if (contentGapScore > 1.0)
        {
            return CompetitionLevel.Low;
        }
        
        // High authority competitors = high competition
        if (avgSubscribers > 500000 || avgViews > 1000000)
        {
            return CompetitionLevel.High;
        }
        
        if (avgSubscribers > 100000 || avgViews > 100000)
        {
            return CompetitionLevel.Medium;
        }
        
        return CompetitionLevel.Low;
    }
}
