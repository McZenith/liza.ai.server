namespace Liza.Infrastructure.Analysis;

using System.Text.RegularExpressions;
using Liza.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for analyzing YouTube recommendation algorithm optimization
/// Helps users create content that gets recommended alongside popular videos
/// </summary>
public interface IRecommendationOptimizationService
{
    Task<RecommendationOptimization> AnalyzeAsync(string keyword, List<EnrichedVideo> videos);
}

public class RecommendationOptimizationService : IRecommendationOptimizationService
{
    private readonly ILogger<RecommendationOptimizationService> _logger;

    public RecommendationOptimizationService(ILogger<RecommendationOptimizationService> logger)
    {
        _logger = logger;
    }

    public Task<RecommendationOptimization> AnalyzeAsync(string keyword, List<EnrichedVideo> videos)
    {
        _logger.LogInformation("Analyzing recommendation optimization for: {Keyword} ({VideoCount} videos)", keyword, videos.Count);
        
        if (!videos.Any())
        {
            return Task.FromResult(new RecommendationOptimization());
        }
        
        // Analyze tag overlaps
        var tagOverlaps = AnalyzeTagOverlaps(videos);
        
        // Find must-use tags (appear in 50%+ of top videos)
        var mustUseTags = FindMustUseTags(videos);
        
        // Extract topic clusters from transcripts
        var topicClusters = ExtractTopicClusters(keyword, videos);
        
        // Identify target videos to appear alongside
        var targetVideos = IdentifyTargetVideos(videos, mustUseTags);
        
        // Calculate topic match score
        var topicMatchScore = CalculateTopicMatchScore(keyword, videos);
        
        // Extract keywords to mention in transcript
        var transcriptKeywords = ExtractTranscriptKeywords(keyword, videos);
        
        return Task.FromResult(new RecommendationOptimization
        {
            TagOverlaps = tagOverlaps,
            MustUseTagsForRecommendation = mustUseTags,
            TopicClusters = topicClusters,
            VideosToAppearAlongside = targetVideos,
            TopicMatchScore = topicMatchScore,
            TranscriptKeywordsToMention = transcriptKeywords
        });
    }

    private List<TagOverlap> AnalyzeTagOverlaps(List<EnrichedVideo> videos)
    {
        var tagStats = new Dictionary<string, (int count, long totalViews)>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var video in videos)
        {
            foreach (var tag in video.Details.Tags)
            {
                var normalizedTag = tag.ToLowerInvariant().Trim();
                if (string.IsNullOrEmpty(normalizedTag) || normalizedTag.Length < 2) continue;
                
                if (!tagStats.TryGetValue(normalizedTag, out var stats))
                {
                    stats = (0, 0);
                }
                tagStats[normalizedTag] = (stats.count + 1, stats.totalViews + video.Details.ViewCount);
            }
        }
        
        return tagStats
            .Where(kv => kv.Value.count >= 2)  // At least 2 videos
            .OrderByDescending(kv => kv.Value.count)
            .ThenByDescending(kv => kv.Value.totalViews)
            .Take(20)
            .Select(kv => new TagOverlap
            {
                Tag = kv.Key,
                UsageCount = kv.Value.count,
                AvgViewsWithTag = kv.Value.count > 0 ? (double)kv.Value.totalViews / kv.Value.count : 0
            })
            .ToList();
    }

    private List<string> FindMustUseTags(List<EnrichedVideo> videos)
    {
        var topVideos = videos.Take(10).ToList();
        if (!topVideos.Any()) return [];
        
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var video in topVideos)
        {
            foreach (var tag in video.Details.Tags)
            {
                var normalizedTag = tag.ToLowerInvariant().Trim();
                if (string.IsNullOrEmpty(normalizedTag)) continue;
                
                tagCounts[normalizedTag] = tagCounts.GetValueOrDefault(normalizedTag) + 1;
            }
        }
        
        // Tags that appear in 50%+ of top 10 videos
        var threshold = Math.Max(topVideos.Count / 2, 2);
        return tagCounts
            .Where(kv => kv.Value >= threshold)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(15)
            .ToList();
    }

    private List<TopicCluster> ExtractTopicClusters(string keyword, List<EnrichedVideo> videos)
    {
        var topicCounts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var keywordLower = keyword.ToLowerInvariant();
        
        foreach (var video in videos.Where(v => v.Transcript != null))
        {
            var text = video.Transcript!.FullText.ToLowerInvariant();
            
            // Extract noun phrases and topics (simplified approach)
            var words = Regex.Split(text, @"\W+")
                .Where(w => w.Length > 3)
                .ToList();
            
            // Find recurring 2-word phrases
            for (int i = 0; i < words.Count - 1; i++)
            {
                var phrase = $"{words[i]} {words[i + 1]}";
                if (IsValidTopic(phrase, keywordLower))
                {
                    if (!topicCounts.TryGetValue(phrase, out var relatedSet))
                    {
                        relatedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        topicCounts[phrase] = relatedSet;
                    }
                    
                    // Collect related terms
                    if (i > 0) relatedSet.Add(words[i - 1]);
                    if (i + 2 < words.Count) relatedSet.Add(words[i + 2]);
                }
            }
        }
        
        return topicCounts
            .OrderByDescending(kv => kv.Value.Count)
            .Take(10)
            .Select(kv => new TopicCluster
            {
                Topic = kv.Key,
                Frequency = kv.Value.Count,
                RelatedTerms = kv.Value.Take(5).ToList()
            })
            .ToList();
    }

    private bool IsValidTopic(string phrase, string keyword)
    {
        // Filter out common non-topics
        var blacklist = new[] { "going to", "want to", "need to", "have to", "going be", "this is", "that is", "you can", "i am", "we are" };
        return !blacklist.Any(b => phrase.Contains(b)) && phrase.Length > 5;
    }

    private List<TargetVideo> IdentifyTargetVideos(List<EnrichedVideo> videos, List<string> mustUseTags)
    {
        // Find videos with highest views that share tags (best recommendation targets)
        return videos
            .OrderByDescending(v => v.Details.ViewCount)
            .Take(20)
            .Select(v =>
            {
                var sharedTags = v.Details.Tags
                    .Where(t => mustUseTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                
                var similarityScore = CalculateVideoSimilarity(v, mustUseTags);
                
                return new TargetVideo
                {
                    VideoId = v.Details.VideoId,
                    Title = v.Details.Title,
                    ViewCount = v.Details.ViewCount,
                    SharedTags = sharedTags,
                    SimilarityScore = similarityScore
                };
            })
            .Where(tv => tv.SimilarityScore > 20)  // Minimum similarity
            .OrderByDescending(tv => tv.ViewCount)
            .Take(5)
            .ToList();
    }

    private int CalculateVideoSimilarity(EnrichedVideo video, List<string> mustUseTags)
    {
        if (!mustUseTags.Any()) return 50;
        
        var sharedCount = video.Details.Tags
            .Count(t => mustUseTags.Contains(t, StringComparer.OrdinalIgnoreCase));
        
        // Percentage of must-use tags that this video has
        var percentage = (double)sharedCount / mustUseTags.Count * 100;
        return Math.Min(100, (int)percentage);
    }

    private int CalculateTopicMatchScore(string keyword, List<EnrichedVideo> videos)
    {
        var keywordLower = keyword.ToLowerInvariant();
        var videosWithTranscripts = videos.Where(v => v.Transcript != null).ToList();
        
        if (!videosWithTranscripts.Any()) return 50;  // Default if no transcripts
        
        // Check how many videos mention the keyword in transcript
        var mentionCount = videosWithTranscripts
            .Count(v => v.Transcript!.FullText.Contains(keywordLower, StringComparison.OrdinalIgnoreCase));
        
        var percentage = (double)mentionCount / videosWithTranscripts.Count * 100;
        return Math.Min(100, (int)percentage);
    }

    private List<string> ExtractTranscriptKeywords(string keyword, List<EnrichedVideo> videos)
    {
        var keywordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var keywordLower = keyword.ToLowerInvariant();
        
        foreach (var video in videos.Where(v => v.Transcript != null).Take(20))
        {
            var text = video.Transcript!.FullText.ToLowerInvariant();
            var words = Regex.Split(text, @"\W+")
                .Where(w => w.Length > 4 && !IsStopWord(w))
                .ToList();
            
            foreach (var word in words.Distinct())
            {
                keywordCounts[word] = keywordCounts.GetValueOrDefault(word) + 1;
            }
            
            // Also extract 2-word phrases
            for (int i = 0; i < words.Count - 1; i++)
            {
                var phrase = $"{words[i]} {words[i + 1]}";
                if (phrase.Length > 8)
                {
                    keywordCounts[phrase] = keywordCounts.GetValueOrDefault(phrase) + 1;
                }
            }
        }
        
        // Return keywords that appear in 30%+ of transcripts
        var threshold = Math.Max(videos.Count(v => v.Transcript != null) / 3, 2);
        
        return keywordCounts
            .Where(kv => kv.Value >= threshold && kv.Key != keywordLower)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(15)
            .ToList();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "again", "also", "back", "because", "before", "being",
        "between", "both", "could", "does", "doing", "down", "during", "each",
        "even", "every", "first", "from", "going", "have", "here", "into",
        "just", "know", "last", "like", "little", "look", "make", "many",
        "more", "most", "much", "never", "next", "only", "other", "over",
        "people", "really", "right", "same", "should", "some", "still", "such",
        "take", "than", "that", "their", "them", "then", "there", "these",
        "they", "thing", "think", "this", "those", "through", "time", "very",
        "want", "well", "were", "what", "when", "where", "which", "while",
        "will", "with", "would", "year", "your", "video", "videos", "channel"
    };

    private bool IsStopWord(string word) => StopWords.Contains(word);
}
