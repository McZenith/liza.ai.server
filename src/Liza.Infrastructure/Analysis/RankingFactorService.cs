namespace Liza.Infrastructure.Analysis;

using System.Text.RegularExpressions;
using Liza.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for analyzing ranking factors
/// Reverse-engineers why certain videos rank higher by correlating keyword placement with position
/// </summary>
public interface IRankingFactorService
{
    Task<RankingInsights> AnalyzeAsync(
        string keyword, 
        List<EnrichedVideo> videos,
        List<string>? youtubeSuggestions = null,
        List<string>? googleSuggestions = null,
        List<ExtractedKeyword>? extractedKeywords = null);
    
    /// <summary>
    /// Calculate ranking signals for a single video explaining why it ranks for a keyword
    /// </summary>
    /// <param name="keyword">The search keyword</param>
    /// <param name="video">The enriched video to analyze</param>
    /// <param name="channelVideos">Optional: other videos from the same channel (for keyword authority)</param>
    VideoRankingSignals CalculateVideoSignals(string keyword, EnrichedVideo video, List<VideoData>? channelVideos = null);
    
    /// <summary>
    /// Analyze correlation between channel keyword authority and ranking position
    /// Call this after CalculateVideoSignals to get the additional correlation factor
    /// </summary>
    RankingFactor AnalyzeChannelKeywordAuthority(List<EnrichedVideo> videosWithSignals);
}

public class RankingFactorService : IRankingFactorService
{
    private readonly ILogger<RankingFactorService> _logger;

    public RankingFactorService(ILogger<RankingFactorService> logger)
    {
        _logger = logger;
    }

    public Task<RankingInsights> AnalyzeAsync(
        string keyword, 
        List<EnrichedVideo> videos,
        List<string>? youtubeSuggestions = null,
        List<string>? googleSuggestions = null,
        List<ExtractedKeyword>? extractedKeywords = null)
    {
        _logger.LogInformation("Analyzing ranking factors for: {Keyword} ({VideoCount} videos)", keyword, videos.Count);
        
        if (!videos.Any())
        {
            return Task.FromResult(new RankingInsights());
        }
        
        var factors = new List<RankingFactor>();
        var keywordLower = keyword.ToLowerInvariant();
        
        // Analyze each factor's correlation with ranking
        factors.Add(AnalyzeTitlePlacement(keywordLower, videos));
        factors.Add(AnalyzeDescriptionPlacement(keywordLower, videos));
        factors.Add(AnalyzeTagUsage(keywordLower, videos));
        factors.Add(AnalyzeTranscriptMentions(keywordLower, videos));
        factors.Add(AnalyzeChannelAuthority(videos));
        factors.Add(AnalyzeEngagement(videos));
        factors.Add(AnalyzeChannelKeywordFocus(keywordLower, videos));  // NEW: Niche channel analysis
        
        // Sort by correlation strength
        var topFactors = factors
            .OrderByDescending(f => Math.Abs(f.Correlation))
            .Take(5)
            .ToList();
        
        // Channel authority analysis
        var channelAuthority = CalculateChannelAuthority(videos);
        
        // Optimal placement analysis
        var optimalPlacement = CalculateOptimalPlacement(keywordLower, videos);
        
        // Generate long-tail variations
        var longTailVariations = GenerateLongTailVariations(
            keyword, channelAuthority, youtubeSuggestions, googleSuggestions, extractedKeywords);
        
        return Task.FromResult(new RankingInsights
        {
            TopFactors = topFactors,
            ChannelAuthority = channelAuthority,
            OptimalPlacement = optimalPlacement,
            LongTailVariations = longTailVariations
        });
    }

    private List<LongTailVariation> GenerateLongTailVariations(
        string keyword,
        ChannelAuthority channelAuthority,
        List<string>? youtubeSuggestions,
        List<string>? googleSuggestions,
        List<ExtractedKeyword>? extractedKeywords)
    {
        var variations = new List<LongTailVariation>();
        var keywordLower = keyword.ToLowerInvariant();
        
        // Calculate base difficulty based on channel authority
        var baseDifficulty = channelAuthority.NeedsEstablishedChannel ? 70 : 40;
        
        // From YouTube suggestions (most valuable - these are what people actually search)
        if (youtubeSuggestions != null)
        {
            foreach (var suggestion in youtubeSuggestions
                .Where(s => s.Length > keyword.Length && 
                           s.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(5))
            {
                // Longer phrases = generally easier to rank
                var lengthBonus = Math.Min((suggestion.Length - keyword.Length) * 2, 30);
                variations.Add(new LongTailVariation
                {
                    Keyword = suggestion,
                    Source = "YouTube Autocomplete",
                    EstimatedDifficulty = Math.Max(10, baseDifficulty - lengthBonus),
                    Reason = "People actively search for this on YouTube"
                });
            }
        }
        
        // From Google suggestions
        if (googleSuggestions != null)
        {
            foreach (var suggestion in googleSuggestions
                .Where(s => s.Length > keyword.Length && 
                           s.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                           !variations.Any(v => v.Keyword.Equals(s, StringComparison.OrdinalIgnoreCase)))
                .Take(3))
            {
                var lengthBonus = Math.Min((suggestion.Length - keyword.Length) * 2, 30);
                variations.Add(new LongTailVariation
                {
                    Keyword = suggestion,
                    Source = "Google Autocomplete",
                    EstimatedDifficulty = Math.Max(15, baseDifficulty - lengthBonus),
                    Reason = "Cross-platform search interest"
                });
            }
        }
        
        // From extracted keywords (phrases from top videos)
        if (extractedKeywords != null)
        {
            foreach (var extracted in extractedKeywords
                .Where(e => e.Keyword.Contains(' ') &&  // Must be a phrase
                           e.Keyword.Contains(keywordLower) &&
                           e.Keyword != keywordLower &&
                           !variations.Any(v => v.Keyword.Equals(e.Keyword, StringComparison.OrdinalIgnoreCase)))
                .Take(3))
            {
                variations.Add(new LongTailVariation
                {
                    Keyword = extracted.Keyword,
                    Source = "Top Video Analysis",
                    EstimatedDifficulty = Math.Max(20, baseDifficulty - 15),
                    Reason = $"Used in {extracted.TotalCount} places across top videos"
                });
            }
        }
        
        // Generate smart combinations if we don't have enough
        if (variations.Count < 3 && extractedKeywords != null)
        {
            var topTerms = extractedKeywords
                .Where(e => !e.Keyword.Contains(' ') && e.Keyword != keywordLower)
                .OrderByDescending(e => e.TfIdfScore)
                .Take(5)
                .Select(e => e.Keyword)
                .ToList();
            
            foreach (var term in topTerms.Take(3 - variations.Count))
            {
                var combo = $"{keyword} {term}";
                if (!variations.Any(v => v.Keyword.Equals(combo, StringComparison.OrdinalIgnoreCase)))
                {
                    variations.Add(new LongTailVariation
                    {
                        Keyword = combo,
                        Source = "AI Generated",
                        EstimatedDifficulty = Math.Max(25, baseDifficulty - 20),
                        Reason = $"'{term}' is a high-value term in this niche"
                    });
                }
            }
        }
        
        return variations
            .OrderBy(v => v.EstimatedDifficulty)
            .Take(10)
            .ToList();
    }

    private RankingFactor AnalyzeTitlePlacement(string keyword, List<EnrichedVideo> videos)
    {
        // Videos with keyword in title - do they rank higher?
        var withKeyword = videos
            .Select((v, i) => new { Video = v, Rank = i + 1 })
            .Where(x => x.Video.Details.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Rank)
            .ToList();
        
        var correlation = CalculatePositionBias(withKeyword, videos.Count);
        
        var recommendation = correlation > 0.3 
            ? $"Include '{keyword}' in your title (strong ranking signal)"
            : correlation > 0 
                ? $"Including '{keyword}' in title may help slightly"
                : "Title keyword placement shows weak correlation";
        
        return new RankingFactor
        {
            Factor = "Keyword in Title",
            Correlation = correlation,
            Recommendation = recommendation
        };
    }

    private RankingFactor AnalyzeDescriptionPlacement(string keyword, List<EnrichedVideo> videos)
    {
        var withKeyword = videos
            .Select((v, i) => new { Video = v, Rank = i + 1 })
            .Where(x => x.Video.Details.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Rank)
            .ToList();
        
        var correlation = CalculatePositionBias(withKeyword, videos.Count);
        
        // Check if first 100 chars matter more
        var inFirst100 = videos
            .Select((v, i) => new { Video = v, Rank = i + 1 })
            .Where(x => x.Video.Details.Description.Length >= 100 && 
                        x.Video.Details.Description[..100].Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Rank)
            .ToList();
        
        var first100Correlation = CalculatePositionBias(inFirst100, videos.Count);
        
        var bestCorrelation = Math.Max(correlation, first100Correlation);
        var recommendation = first100Correlation > correlation
            ? $"Put '{keyword}' in the first 100 characters of your description"
            : $"Mention '{keyword}' in your description";
        
        return new RankingFactor
        {
            Factor = "Keyword in Description",
            Correlation = bestCorrelation,
            Recommendation = recommendation
        };
    }

    private RankingFactor AnalyzeTagUsage(string keyword, List<EnrichedVideo> videos)
    {
        var withTag = videos
            .Select((v, i) => new { Video = v, Rank = i + 1 })
            .Where(x => x.Video.Details.Tags.Any(t => 
                t.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.Rank)
            .ToList();
        
        var correlation = CalculatePositionBias(withTag, videos.Count);
        
        return new RankingFactor
        {
            Factor = "Keyword in Tags",
            Correlation = correlation,
            Recommendation = correlation > 0.2 
                ? $"Use '{keyword}' as a tag"
                : "Tags show moderate importance for this keyword"
        };
    }

    private RankingFactor AnalyzeTranscriptMentions(string keyword, List<EnrichedVideo> videos)
    {
        var videosWithTranscripts = videos
            .Select((v, i) => new { Video = v, Rank = i + 1 })
            .Where(x => x.Video.Transcript != null && !string.IsNullOrEmpty(x.Video.Transcript.FullText))
            .ToList();
        
        if (!videosWithTranscripts.Any())
        {
            return new RankingFactor
            {
                Factor = "Keyword in Transcript",
                Correlation = 0,
                Recommendation = "Insufficient transcript data to analyze"
            };
        }
        
        // Count mentions and correlate with rank
        var mentionData = videosWithTranscripts
            .Select(x => new 
            {
                x.Rank,
                Mentions = Regex.Matches(
                    x.Video.Transcript!.FullText, 
                    Regex.Escape(keyword), 
                    RegexOptions.IgnoreCase).Count
            })
            .Where(x => x.Mentions > 0)
            .ToList();
        
        // Higher mentions in top ranked videos = positive correlation
        if (!mentionData.Any())
        {
            return new RankingFactor
            {
                Factor = "Keyword in Transcript",
                Correlation = 0,
                Recommendation = "Keyword not found in transcripts (consider mentioning it verbally)"
            };
        }
        
        var avgRankWithMentions = mentionData.Average(x => x.Rank);
        var avgRankOverall = videos.Count / 2.0;
        var correlation = (avgRankOverall - avgRankWithMentions) / avgRankOverall;
        
        return new RankingFactor
        {
            Factor = "Keyword in Transcript",
            Correlation = Math.Round(correlation, 2),
            Recommendation = correlation > 0.1 
                ? $"Mention '{keyword}' naturally in your video (strong signal)"
                : "Transcript mentions show weak correlation"
        };
    }

    private RankingFactor AnalyzeChannelAuthority(List<EnrichedVideo> videos)
    {
        var videosWithChannel = videos
            .Select((v, i) => new { Video = v, Rank = i + 1 })
            .Where(x => x.Video.Channel != null)
            .ToList();
        
        if (!videosWithChannel.Any())
        {
            return new RankingFactor
            {
                Factor = "Channel Subscribers",
                Correlation = 0,
                Recommendation = "Insufficient channel data"
            };
        }
        
        // Check if higher subscriber channels rank better
        var topHalfAvgSubs = videosWithChannel
            .Take(videosWithChannel.Count / 2)
            .Average(x => x.Video.Channel!.SubscriberCount);
        
        var bottomHalfAvgSubs = videosWithChannel
            .Skip(videosWithChannel.Count / 2)
            .Average(x => x.Video.Channel!.SubscriberCount);
        
        var correlation = topHalfAvgSubs > bottomHalfAvgSubs ? 0.5 : -0.1;
        
        return new RankingFactor
        {
            Factor = "Channel Subscribers",
            Correlation = correlation,
            Recommendation = correlation > 0.3 
                ? "Established channels dominate this keyword - consider long-tail variations"
                : "Channel size is less important for this keyword (good opportunity!)"
        };
    }

    private RankingFactor AnalyzeEngagement(List<EnrichedVideo> videos)
    {
        // Check if like ratio correlates with rank
        var engagementData = videos
            .Select((v, i) => new 
            { 
                Rank = i + 1, 
                LikeRatio = v.Details.ViewCount > 0 
                    ? (double)v.Details.LikeCount / v.Details.ViewCount 
                    : 0
            })
            .ToList();
        
        var topHalfAvgEngagement = engagementData.Take(engagementData.Count / 2).Average(x => x.LikeRatio);
        var bottomHalfAvgEngagement = engagementData.Skip(engagementData.Count / 2).Average(x => x.LikeRatio);
        
        var correlation = topHalfAvgEngagement > bottomHalfAvgEngagement ? 0.4 : -0.1;
        
        return new RankingFactor
        {
            Factor = "Like/View Ratio",
            Correlation = correlation,
            Recommendation = "Create engaging content that encourages likes and comments"
        };
    }

    private double CalculatePositionBias(List<int> ranks, int totalVideos)
    {
        if (!ranks.Any() || totalVideos == 0) return 0;
        
        // If videos with the factor rank better than average, correlation is positive
        var avgRank = ranks.Average();
        var expectedAvgRank = (totalVideos + 1) / 2.0;
        
        // Normalize to -1 to +1 range
        var bias = (expectedAvgRank - avgRank) / expectedAvgRank;
        return Math.Round(Math.Max(-1, Math.Min(1, bias)), 2);
    }

    private ChannelAuthority CalculateChannelAuthority(List<EnrichedVideo> videos)
    {
        var channels = videos
            .Where(v => v.Channel != null)
            .Select(v => v.Channel!)
            .ToList();
        
        if (!channels.Any())
        {
            return new ChannelAuthority
            {
                AvgSubscribers = 0,
                NeedsEstablishedChannel = false,
                MinSubscribersToRank = 0
            };
        }
        
        var avgSubs = (long)channels.Average(c => c.SubscriberCount);
        var top10Subs = channels.Take(10).Average(c => c.SubscriberCount);
        
        return new ChannelAuthority
        {
            AvgSubscribers = avgSubs,
            NeedsEstablishedChannel = avgSubs > 100000,
            MinSubscribersToRank = (int)(top10Subs * 0.1)  // Rough estimate
        };
    }

    private KeywordPlacement CalculateOptimalPlacement(string keyword, List<EnrichedVideo> videos)
    {
        // Analyze top 10 videos for patterns
        var topVideos = videos.Take(10).ToList();
        
        // Title: keyword in first 3 words?
        var inFirst3Words = topVideos.Count(v =>
        {
            var words = v.Details.Title.Split(' ').Take(3);
            return words.Any(w => w.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        });
        
        // Description: keyword in first 100 chars?
        var inFirst100 = topVideos.Count(v =>
            v.Details.Description.Length >= 100 &&
            v.Details.Description[..100].Contains(keyword, StringComparison.OrdinalIgnoreCase));
        
        // Tags: average tag count
        var avgTagCount = topVideos.Average(v => v.Details.Tags.Count);
        
        // Transcript density
        var densities = topVideos
            .Where(v => v.Transcript != null && v.Details.Duration.TotalMinutes > 0)
            .Select(v =>
            {
                var mentions = Regex.Matches(v.Transcript!.FullText, Regex.Escape(keyword), RegexOptions.IgnoreCase).Count;
                return mentions / v.Details.Duration.TotalMinutes;
            })
            .ToList();
        
        return new KeywordPlacement
        {
            InTitleFirst3Words = inFirst3Words >= 5,  // 50%+ of top 10
            InDescriptionFirst100Chars = inFirst100 >= 5,
            OptimalTagCount = (int)avgTagCount,
            TranscriptMentionDensity = densities.Any() ? densities.Average() : 0
        };
    }

    /// <summary>
    /// Calculate ranking signals for a single video explaining why it ranks for a keyword
    /// </summary>
    public VideoRankingSignals CalculateVideoSignals(string keyword, EnrichedVideo video, List<VideoData>? channelVideos = null)
    {
        var keywordLower = keyword.ToLowerInvariant();
        var keywordParts = keywordLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var reasons = new List<string>();
        
        // Title analysis
        var titleLower = video.Details.Title.ToLowerInvariant();
        var keywordInTitle = titleLower.Contains(keywordLower);
        var words = video.Details.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keywordInFirst3Words = words.Take(3)
            .Any(w => w.Contains(keywordLower, StringComparison.OrdinalIgnoreCase));
        
        if (keywordInFirst3Words)
            reasons.Add("🎯 Keyword in title (first 3 words)");
        else if (keywordInTitle)
            reasons.Add("📝 Keyword in title");
        
        // Description analysis
        var descLower = video.Details.Description.ToLowerInvariant();
        var keywordInDesc = descLower.Contains(keywordLower);
        if (keywordInDesc)
            reasons.Add("📄 Keyword in description");
        
        // Tag analysis
        var tagMatchCount = video.Details.Tags
            .Count(t => t.Contains(keywordLower, StringComparison.OrdinalIgnoreCase));
        if (tagMatchCount > 0)
            reasons.Add($"🏷️ {tagMatchCount} matching tag{(tagMatchCount > 1 ? "s" : "")}");
        
        // Transcript analysis
        var transcriptMentions = 0;
        if (video.Transcript != null && !string.IsNullOrEmpty(video.Transcript.FullText))
        {
            transcriptMentions = Regex.Matches(
                video.Transcript.FullText, 
                Regex.Escape(keywordLower), 
                RegexOptions.IgnoreCase).Count;
            
            if (transcriptMentions > 0)
                reasons.Add($"🎙️ Mentioned {transcriptMentions}x in transcript");
        }
        
        // Engagement analysis
        var engagementRate = video.Details.ViewCount > 0 
            ? (double)video.Details.LikeCount / video.Details.ViewCount * 100 
            : 0;
        
        if (engagementRate >= 5)
            reasons.Add($"👍 High engagement ({engagementRate:F1}% like rate)");
        else if (engagementRate >= 3)
            reasons.Add($"👍 Good engagement ({engagementRate:F1}% like rate)");
        
        // Channel authority analysis
        var subscriberCount = video.Channel?.SubscriberCount ?? 0;
        var authorityTier = GetAuthorityTier(subscriberCount);
        
        if (authorityTier >= 4)
            reasons.Add($"📺 Large channel ({FormatSubscribers(subscriberCount)} subs)");
        else if (authorityTier >= 3)
            reasons.Add($"📺 Established channel ({FormatSubscribers(subscriberCount)} subs)");
        
        // ===== Channel-level keyword signals =====
        var keywordInChannelName = false;
        var keywordInChannelKeywords = false;
        var channelKeywordMatchCount = 0;
        var isNicheChannel = false;
        var keywordInChannelDescription = false;
        
        if (video.Channel != null)
        {
            var channelNameLower = video.Channel.Title.ToLowerInvariant();
            
            // Check if keyword is in channel name
            keywordInChannelName = channelNameLower.Contains(keywordLower) ||
                keywordParts.Any(part => channelNameLower.Contains(part) && part.Length > 3);
            
            if (keywordInChannelName)
                reasons.Add("🏠 Keyword in channel name (niche authority)");
            
            // Check channel description/about section
            if (!string.IsNullOrEmpty(video.Channel.Description))
            {
                var channelDescLower = video.Channel.Description.ToLowerInvariant();
                keywordInChannelDescription = channelDescLower.Contains(keywordLower) ||
                    keywordParts.Any(part => channelDescLower.Contains(part) && part.Length > 3);
                
                if (keywordInChannelDescription)
                    reasons.Add("📋 Keyword in channel description (topical channel)");
            }
            
            // Check channel keywords
            if (video.Channel.Keywords.Any())
            {
                channelKeywordMatchCount = video.Channel.Keywords
                    .Count(ck => ck.Contains(keywordLower, StringComparison.OrdinalIgnoreCase) ||
                                 keywordParts.Any(part => ck.Contains(part, StringComparison.OrdinalIgnoreCase) && part.Length > 3));
                
                keywordInChannelKeywords = channelKeywordMatchCount > 0;
                
                if (channelKeywordMatchCount >= 3)
                    reasons.Add($"🎪 Niche-focused channel ({channelKeywordMatchCount} matching keywords)");
                else if (channelKeywordMatchCount > 0)
                    reasons.Add($"📌 Channel keywords match ({channelKeywordMatchCount})");
            }
            
            // Determine if this is a "niche channel" for this keyword
            // Niche = keyword in channel name OR keyword in description OR 2+ channel keywords match
            isNicheChannel = keywordInChannelName || keywordInChannelDescription || channelKeywordMatchCount >= 2;
            
            if (isNicheChannel && !keywordInChannelName && !keywordInChannelDescription && channelKeywordMatchCount < 3)
                reasons.Add("🌟 Topic-focused channel");
        }
        
        // ===== Comment analysis (audience engagement with keyword) =====
        var commentKeywordMentions = 0;
        var audienceDiscussesKeyword = false;
        
        if (video.Comments.Any())
        {
            commentKeywordMentions = video.Comments
                .Count(c => c.Text.Contains(keywordLower, StringComparison.OrdinalIgnoreCase) ||
                           keywordParts.Any(part => c.Text.Contains(part, StringComparison.OrdinalIgnoreCase) && part.Length > 3));
            
            audienceDiscussesKeyword = commentKeywordMentions >= 3;
            
            if (audienceDiscussesKeyword)
                reasons.Add($"💬 Audience discusses keyword ({commentKeywordMentions} comments)");
            else if (commentKeywordMentions > 0)
                reasons.Add($"💬 Keyword in {commentKeywordMentions} comment{(commentKeywordMentions > 1 ? "s" : "")}");
        }
        
        // ===== Channel keyword authority (videos with keyword on channel over time) =====
        var channelVideosAnalyzed = 0;
        var channelVideosWithKeyword = 0;
        var channelKeywordRatio = 0.0;
        var isKeywordAuthority = false;
        
        if (channelVideos != null && channelVideos.Any())
        {
            channelVideosAnalyzed = channelVideos.Count;
            
            // Count videos with keyword in title or tags
            channelVideosWithKeyword = channelVideos.Count(cv =>
            {
                var cvTitleLower = cv.Title.ToLowerInvariant();
                var titleMatch = cvTitleLower.Contains(keywordLower) ||
                    keywordParts.Any(part => cvTitleLower.Contains(part) && part.Length > 3);
                    
                var tagMatch = cv.Tags.Any(t => t.Contains(keywordLower, StringComparison.OrdinalIgnoreCase) ||
                    keywordParts.Any(part => t.Contains(part, StringComparison.OrdinalIgnoreCase) && part.Length > 3));
                    
                return titleMatch || tagMatch;
            });
            
            channelKeywordRatio = (double)channelVideosWithKeyword / channelVideosAnalyzed * 100;
            isKeywordAuthority = channelKeywordRatio >= 30;  // 30%+ of videos about this keyword
            
            if (isKeywordAuthority)
                reasons.Add($"👑 Keyword authority ({channelVideosWithKeyword}/{channelVideosAnalyzed} videos = {channelKeywordRatio:F0}%)");
            else if (channelVideosWithKeyword >= 5)
                reasons.Add($"📚 Consistent keyword coverage ({channelVideosWithKeyword} videos)");
            else if (channelVideosWithKeyword > 0)
                reasons.Add($"📁 {channelVideosWithKeyword} other video{(channelVideosWithKeyword > 1 ? "s" : "")} on topic");
        }
        
        // View count as social proof
        if (video.Details.ViewCount >= 1_000_000)
            reasons.Add($"🔥 {video.Details.ViewCount / 1_000_000.0:F1}M views");
        else if (video.Details.ViewCount >= 100_000)
            reasons.Add($"📈 {video.Details.ViewCount / 1_000.0:F0}K views");
        
        return new VideoRankingSignals
        {
            KeywordInTitle = keywordInTitle,
            KeywordInFirst3Words = keywordInFirst3Words,
            KeywordInDescription = keywordInDesc,
            TagMatchCount = tagMatchCount,
            TranscriptMentions = transcriptMentions,
            EngagementRate = Math.Round(engagementRate, 2),
            ChannelAuthorityTier = authorityTier,
            KeywordInChannelName = keywordInChannelName,
            KeywordInChannelKeywords = keywordInChannelKeywords,
            ChannelKeywordMatchCount = channelKeywordMatchCount,
            IsNicheChannel = isNicheChannel,
            KeywordInChannelDescription = keywordInChannelDescription,
            CommentKeywordMentions = commentKeywordMentions,
            AudienceDiscussesKeyword = audienceDiscussesKeyword,
            ChannelVideosAnalyzed = channelVideosAnalyzed,
            ChannelVideosWithKeyword = channelVideosWithKeyword,
            ChannelKeywordRatio = Math.Round(channelKeywordRatio, 1),
            IsKeywordAuthority = isKeywordAuthority,
            RankingReasons = reasons
        };
    }
    
    /// <summary>
    /// Analyze if channels focused on the keyword rank better
    /// </summary>
    private RankingFactor AnalyzeChannelKeywordFocus(string keyword, List<EnrichedVideo> videos)
    {
        var keywordLower = keyword.ToLowerInvariant();
        var keywordParts = keywordLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var videosWithChannel = videos
            .Select((v, i) => new { Video = v, Rank = i + 1 })
            .Where(x => x.Video.Channel != null)
            .ToList();
        
        if (!videosWithChannel.Any())
        {
            return new RankingFactor
            {
                Factor = "Channel Niche Focus",
                Correlation = 0,
                Recommendation = "Insufficient channel data to analyze"
            };
        }
        
        // Find videos from niche-focused channels
        var nicheChannelVideos = videosWithChannel
            .Where(x =>
            {
                var channelNameLower = x.Video.Channel!.Title.ToLowerInvariant();
                var nameMatch = channelNameLower.Contains(keywordLower) ||
                    keywordParts.Any(part => channelNameLower.Contains(part) && part.Length > 3);
                
                var keywordMatch = x.Video.Channel.Keywords.Any() && x.Video.Channel.Keywords
                    .Any(ck => ck.Contains(keywordLower, StringComparison.OrdinalIgnoreCase) ||
                               keywordParts.Any(part => ck.Contains(part, StringComparison.OrdinalIgnoreCase) && part.Length > 3));
                
                return nameMatch || keywordMatch;
            })
            .Select(x => x.Rank)
            .ToList();
        
        var correlation = CalculatePositionBias(nicheChannelVideos, videos.Count);
        
        var nicheCount = nicheChannelVideos.Count;
        var nichePercent = videos.Count > 0 ? (nicheCount * 100 / videos.Count) : 0;
        
        var recommendation = correlation > 0.2
            ? $"Niche channels dominate this keyword ({nichePercent}% of top results) - build authority in this topic"
            : nicheCount > 0
                ? $"Some niche channels rank ({nicheCount} of {videos.Count}) - topic focus helps but isn't required"
                : "General channels can rank for this keyword - niche focus is optional";
        
        return new RankingFactor
        {
            Factor = "Channel Niche Focus",
            Correlation = correlation,
            Recommendation = recommendation
        };
    }
    
    private static int GetAuthorityTier(long subscribers)
    {
        return subscribers switch
        {
            >= 10_000_000 => 5,  // 10M+
            >= 1_000_000 => 4,   // 1M+
            >= 100_000 => 3,     // 100K+
            >= 10_000 => 2,      // 10K+
            _ => 1               // <10K
        };
    }
    
    private static string FormatSubscribers(long count)
    {
        return count switch
        {
            >= 1_000_000 => $"{count / 1_000_000.0:F1}M",
            >= 1_000 => $"{count / 1_000.0:F0}K",
            _ => count.ToString()
        };
    }
    
    /// <summary>
    /// Analyze correlation between channel keyword authority and ranking position
    /// Uses videos that already have RankingSignals calculated with channel video data
    /// </summary>
    public RankingFactor AnalyzeChannelKeywordAuthority(List<EnrichedVideo> videosWithSignals)
    {
        // Filter to videos with valid keyword authority data
        var videosWithAuthority = videosWithSignals
            .Select((v, i) => new { Video = v, Rank = i + 1, Signals = v.RankingSignals })
            .Where(x => x.Signals != null && x.Signals.ChannelVideosAnalyzed > 0)
            .ToList();
        
        if (videosWithAuthority.Count < 2)
        {
            return new RankingFactor
            {
                Factor = "Channel Keyword Authority",
                Correlation = 0,
                Recommendation = "Insufficient data to analyze channel keyword authority pattern"
            };
        }
        
        // Calculate correlation: do higher keyword ratios correlate with better (lower) ranks?
        var authorityVideos = videosWithAuthority
            .Where(x => x.Signals!.IsKeywordAuthority)
            .Select(x => x.Rank)
            .ToList();
        
        // Calculate average rank of authority channels vs non-authority
        var avgAuthorityRank = authorityVideos.Any() ? authorityVideos.Average() : 0;
        var avgNonAuthorityRank = videosWithAuthority
            .Where(x => !x.Signals!.IsKeywordAuthority)
            .Select(x => x.Rank)
            .DefaultIfEmpty(0)
            .Average();
        
        // Calculate correlation based on average keyword ratio by position
        var avgKeywordRatio = videosWithAuthority.Average(x => x.Signals!.ChannelKeywordRatio);
        var top3AvgRatio = videosWithAuthority.Take(3).Average(x => x.Signals!.ChannelKeywordRatio);
        
        // Positive correlation if top 3 have higher than average keyword ratio
        var correlation = 0.0;
        if (avgKeywordRatio > 0)
        {
            correlation = (top3AvgRatio - avgKeywordRatio) / Math.Max(avgKeywordRatio, 1) * 0.5;
            correlation = Math.Round(Math.Max(-1, Math.Min(1, correlation)), 2);
        }
        
        // Generate insights
        var authorityCount = authorityVideos.Count;
        var totalWithData = videosWithAuthority.Count;
        var authorityPercent = totalWithData > 0 ? authorityCount * 100 / totalWithData : 0;
        
        string recommendation;
        if (correlation > 0.2)
        {
            recommendation = $"Keyword authority strongly correlates with ranking ({authorityPercent}% of top channels have 30%+ videos on topic) - build a content library around this keyword";
        }
        else if (authorityCount > 0)
        {
            recommendation = $"Some keyword authorities rank ({authorityCount} of {totalWithData}), but general channels can also succeed - consistent content helps but isn't required";
        }
        else
        {
            recommendation = "No keyword authorities in top results - this is an open opportunity for newer channels to compete";
        }
        
        return new RankingFactor
        {
            Factor = "Channel Keyword Authority",
            Correlation = correlation,
            Recommendation = recommendation
        };
    }
}
