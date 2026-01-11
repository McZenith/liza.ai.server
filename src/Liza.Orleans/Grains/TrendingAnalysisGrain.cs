namespace Liza.Orleans.Grains;

using Liza.Core.Models;
using Liza.Core.Services;
using Liza.Orleans.Grains.Abstractions;
using Microsoft.Extensions.Logging;
using global::Orleans.Runtime;
using System.Text.RegularExpressions;

/// <summary>
/// Grain that manages trending keyword analysis and caching for the Explore tab.
/// Called daily by the scheduled worker to pre-warm data.
/// </summary>
public class TrendingAnalysisGrain : Grain, ITrendingAnalysisGrain
{
    private readonly IGrainFactory _grainFactory;
    private readonly IYouTubeService _youtubeService;
    private readonly ILogger<TrendingAnalysisGrain> _logger;
    private readonly IPersistentState<TrendingCacheState> _cache;

    public TrendingAnalysisGrain(
        [PersistentState("trendingCache", "Default")]
        IPersistentState<TrendingCacheState> cache,
        IGrainFactory grainFactory,
        IYouTubeService youtubeService,
        ILogger<TrendingAnalysisGrain> logger)
    {
        _cache = cache;
        _grainFactory = grainFactory;
        _youtubeService = youtubeService;
        _logger = logger;
    }

    public async Task WarmupAsync()
    {
        var regionCode = this.GetPrimaryKeyString();
        _logger.LogInformation("Starting trending keyword warmup for region: {Region}", regionCode);

        try
        {
            // Step 1: Fetch trending videos from YouTube
            var trendingVideos = await _youtubeService.GetTrendingVideosAsync(regionCode);
            _logger.LogInformation("Fetched {Count} trending videos for region {Region}", 
                trendingVideos.Count, regionCode);

            if (trendingVideos.Count == 0)
            {
                _logger.LogWarning("No trending videos found for region: {Region}", regionCode);
                return;
            }

            // Step 2: Extract keywords from video metadata
            var extractedKeywords = ExtractKeywordsFromVideos(trendingVideos);
            _logger.LogInformation("Extracted {Count} unique keywords from trending videos", 
                extractedKeywords.Count);

            // Step 3: Analyze top keywords (limit to 20 to avoid API rate limits)
            var keywordsToAnalyze = extractedKeywords.Take(20).ToList();
            var results = new List<TrendingKeywordSummary>();

            foreach (var keyword in keywordsToAnalyze)
            {
                try
                {
                    var grain = _grainFactory.GetGrain<IKeywordAnalysisGrain>(keyword.Keyword);
                    var analysis = await grain.AnalyzeAsync();

                    results.Add(new TrendingKeywordSummary
                    {
                        Keyword = keyword.Keyword,
                        Grade = analysis.Scores.Grade,
                        Opportunity = analysis.Scores.Opportunity,
                        Difficulty = analysis.Scores.Difficulty,
                        SearchVolume = analysis.SearchDemand.Volume,
                        TrendingVideoCount = keyword.VideoCount,
                        TopVideoTitle = keyword.TopVideoTitle,
                        TopVideoThumbnail = keyword.TopVideoThumbnail,
                        AnalyzedAt = DateTime.UtcNow
                    });

                    _logger.LogDebug("Analyzed keyword: {Keyword} - Grade: {Grade}", 
                        keyword.Keyword, analysis.Scores.Grade);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to analyze keyword: {Keyword}", keyword.Keyword);
                }
            }

            // Step 4: Cache videos
            _cache.State.Videos = trendingVideos.Take(50).Select(v => new CachedTrendingVideo
            {
                VideoId = v.VideoId,
                Title = v.Title,
                ChannelTitle = v.ChannelTitle,
                ViewCount = v.ViewCount,
                LikeCount = v.LikeCount,
                ThumbnailMedium = v.Thumbnails.Medium,
                ThumbnailHigh = v.Thumbnails.High,
                PublishedAt = v.PublishedAt
            }).ToList();

            // Step 5: Cache keywords
            _cache.State.Keywords = results
                .OrderByDescending(k => k.Opportunity - k.Difficulty)
                .ThenByDescending(k => k.TrendingVideoCount)
                .ToList();
            _cache.State.LastWarmupAt = DateTime.UtcNow;
            _cache.State.RegionCode = regionCode;
            await _cache.WriteStateAsync();

            _logger.LogInformation(
                "Trending warmup complete for {Region}: {Keywords} keywords, {Videos} videos cached at {Time}", 
                regionCode, results.Count, _cache.State.Videos.Count, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warm up trending keywords for region: {Region}", regionCode);
            throw;
        }
    }

    public Task<List<TrendingKeywordSummary>> GetCachedTrendingKeywordsAsync()
    {
        if (_cache.State.Keywords.Count == 0)
        {
            _logger.LogInformation("No cached trending keywords available for region: {Region}", 
                this.GetPrimaryKeyString());
        }

        return Task.FromResult(_cache.State.Keywords);
    }

    public Task<List<CachedTrendingVideo>> GetCachedTrendingVideosAsync()
    {
        if (_cache.State.Videos.Count == 0)
        {
            _logger.LogInformation("No cached trending videos available for region: {Region}", 
                this.GetPrimaryKeyString());
        }

        return Task.FromResult(_cache.State.Videos);
    }

    /// <summary>
    /// Extract keywords from video titles, tags, and descriptions
    /// </summary>
    private List<ExtractedKeyword> ExtractKeywordsFromVideos(List<VideoData> videos)
    {
        var keywordCounts = new Dictionary<string, ExtractedKeyword>(StringComparer.OrdinalIgnoreCase);

        foreach (var video in videos)
        {
            // Extract from title (highest weight)
            var titleKeywords = ExtractKeywordsFromText(video.Title);
            foreach (var kw in titleKeywords)
            {
                AddOrUpdateKeyword(keywordCounts, kw, video, 3);
            }

            // Extract from tags
            if (video.Tags.Count > 0)
            {
                foreach (var tag in video.Tags.Take(10))
                {
                    var normalizedTag = NormalizeKeyword(tag);
                    if (!string.IsNullOrWhiteSpace(normalizedTag) && normalizedTag.Length >= 3)
                    {
                        AddOrUpdateKeyword(keywordCounts, normalizedTag, video, 2);
                    }
                }
            }

            // Extract from description (first 500 chars only)
            if (!string.IsNullOrEmpty(video.Description))
            {
                var descSnippet = video.Description.Length > 500 
                    ? video.Description[..500] 
                    : video.Description;
                var descKeywords = ExtractKeywordsFromText(descSnippet);
                foreach (var kw in descKeywords.Take(5))
                {
                    AddOrUpdateKeyword(keywordCounts, kw, video, 1);
                }
            }
        }

        // Sort by weighted score and return
        return keywordCounts.Values
            .Where(k => k.VideoCount >= 2) // Must appear in at least 2 videos
            .OrderByDescending(k => k.Score)
            .ThenByDescending(k => k.VideoCount)
            .ToList();
    }

    private void AddOrUpdateKeyword(
        Dictionary<string, ExtractedKeyword> dict, 
        string keyword, 
        VideoData video, 
        int weight)
    {
        if (!dict.TryGetValue(keyword, out var existing))
        {
            dict[keyword] = new ExtractedKeyword
            {
                Keyword = keyword,
                VideoCount = 1,
                Score = weight,
                TopVideoTitle = video.Title,
                TopVideoThumbnail = video.Thumbnails.Medium
            };
        }
        else
        {
            existing.VideoCount++;
            existing.Score += weight;
        }
    }

    private List<string> ExtractKeywordsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // Remove special characters, keep alphanumeric and spaces
        var cleaned = Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", " ");
        
        // Split into words and filter
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .ToList();

        // Generate 2-3 word phrases (more valuable than single words)
        var phrases = new List<string>();
        for (int i = 0; i < words.Count; i++)
        {
            // Single keyword
            phrases.Add(words[i]);

            // 2-word phrase
            if (i < words.Count - 1)
            {
                phrases.Add($"{words[i]} {words[i + 1]}");
            }

            // 3-word phrase
            if (i < words.Count - 2)
            {
                phrases.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
            }
        }

        return phrases.Distinct().Take(10).ToList();
    }

    private static string NormalizeKeyword(string keyword)
    {
        return Regex.Replace(keyword.ToLowerInvariant().Trim(), @"\s+", " ");
    }

    // Common stop words to filter out
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
        "by", "from", "as", "is", "was", "are", "were", "been", "be", "have", "has", "had",
        "do", "does", "did", "will", "would", "could", "should", "may", "might", "must",
        "this", "that", "these", "those", "i", "you", "he", "she", "it", "we", "they",
        "my", "your", "his", "her", "its", "our", "their", "what", "which", "who", "whom",
        "how", "when", "where", "why", "all", "each", "every", "both", "few", "more",
        "most", "other", "some", "such", "no", "not", "only", "same", "so", "than", "too",
        "very", "just", "about", "into", "through", "during", "before", "after", "above",
        "below", "between", "under", "again", "further", "then", "once", "here", "there",
        "new", "video", "official", "full", "watch", "best", "top", "first", "last"
    };

    private class ExtractedKeyword
    {
        public required string Keyword { get; init; }
        public int VideoCount { get; set; }
        public int Score { get; set; }
        public string? TopVideoTitle { get; init; }
        public string? TopVideoThumbnail { get; init; }
    }
}

/// <summary>
/// Persistent state for trending keyword cache
/// </summary>
[GenerateSerializer]
public class TrendingCacheState
{
    [Id(0)] public List<TrendingKeywordSummary> Keywords { get; set; } = [];
    [Id(1)] public DateTime LastWarmupAt { get; set; }
    [Id(2)] public string RegionCode { get; set; } = "US";
    [Id(3)] public List<CachedTrendingVideo> Videos { get; set; } = [];
}
