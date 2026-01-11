namespace Liza.Infrastructure.YouTube;

using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google;
using Liza.Core.Models;
using Liza.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class YouTubeServiceOptions
{
    /// <summary>
    /// Comma-separated list of API keys for rotation
    /// </summary>
    public required string ApiKeys { get; set; }
    
    /// <summary>
    /// Legacy single key support (deprecated, use ApiKeys)
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// YouTube Data API service with automatic key rotation on quota exceeded
/// </summary>
public class YouTubeDataService : IYouTubeService
{
    private readonly string[] _apiKeys;
    private readonly ILogger<YouTubeDataService> _logger;
    
    // Static to persist key position across all service instances
    private static int _currentKeyIndex = 0;
    private static readonly object _keyLock = new();

    public YouTubeDataService(
        IOptions<YouTubeServiceOptions> options,
        ILogger<YouTubeDataService> logger)
    {
        _logger = logger;
        
        // Support both new ApiKeys (comma-separated) and legacy ApiKey
        var keysConfig = options.Value.ApiKeys ?? options.Value.ApiKey ?? "";
        _apiKeys = keysConfig
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToArray();
        
        if (_apiKeys.Length == 0)
        {
            throw new InvalidOperationException("No YouTube API keys configured. Set YouTube:ApiKeys in appsettings.json");
        }
        
        _logger.LogInformation("YouTube API initialized with {KeyCount} API keys", _apiKeys.Length);
    }

    private YouTubeService CreateService(string apiKey)
    {
        return new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = apiKey,
            ApplicationName = "Liza.ai"
        });
    }

    private string GetCurrentKey()
    {
        lock (_keyLock)
        {
            return _apiKeys[_currentKeyIndex];
        }
    }

    private string RotateToNextKey()
    {
        lock (_keyLock)
        {
            _currentKeyIndex = (_currentKeyIndex + 1) % _apiKeys.Length;
            _logger.LogWarning("Rotated to YouTube API key {Index}/{Total}", _currentKeyIndex + 1, _apiKeys.Length);
            return _apiKeys[_currentKeyIndex];
        }
    }

    private async Task<T> ExecuteWithRotationAsync<T>(Func<YouTubeService, Task<T>> operation, string operationName)
    {
        var triedKeys = 0;
        var startKeyIndex = _currentKeyIndex;

        while (triedKeys < _apiKeys.Length)
        {
            var currentKey = GetCurrentKey();
            var youtube = CreateService(currentKey);

            try
            {
                return await operation(youtube);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden && 
                                                  ex.Error?.Errors?.Any(e => e.Reason == "quotaExceeded") == true)
            {
                _logger.LogWarning("YouTube API quota exceeded for key {Index}, rotating...", _currentKeyIndex + 1);
                RotateToNextKey();
                triedKeys++;
                
                // If we've gone full circle, throw
                if (_currentKeyIndex == startKeyIndex)
                {
                    _logger.LogError("All {Count} YouTube API keys have exceeded quota", _apiKeys.Length);
                    throw new InvalidOperationException($"All {_apiKeys.Length} YouTube API keys have exceeded their quota. Please wait for quota reset or add more keys.");
                }
            }
        }

        throw new InvalidOperationException("All YouTube API keys exhausted");
    }

    public async Task<List<VideoData>> SearchVideosAsync(string query, int maxResults = 50)
    {
        _logger.LogInformation("Searching YouTube for: {Query}", query);

        return await ExecuteWithRotationAsync(async youtube =>
        {
            var request = youtube.Search.List("snippet");
            request.Q = query;
            request.Type = "video";
            request.MaxResults = maxResults;
            request.Order = SearchResource.ListRequest.OrderEnum.Relevance;

            var response = await request.ExecuteAsync();

            var videoIds = response.Items
                .Where(i => i.Id.VideoId != null)
                .Select(i => i.Id.VideoId!)
                .ToList();

            // Get full details for these videos
            return await GetVideoDetailsBatchInternalAsync(youtube, videoIds);
        }, "SearchVideos");
    }

    public async Task<VideoData?> GetVideoDetailsAsync(string videoId)
    {
        var results = await GetVideoDetailsBatchAsync([videoId]);
        return results.FirstOrDefault();
    }

    public async Task<List<VideoData>> GetVideoDetailsBatchAsync(IEnumerable<string> videoIds)
    {
        var ids = videoIds.ToList();
        if (ids.Count == 0) return [];

        return await ExecuteWithRotationAsync(
            youtube => GetVideoDetailsBatchInternalAsync(youtube, ids),
            "GetVideoDetails");
    }

    private async Task<List<VideoData>> GetVideoDetailsBatchInternalAsync(YouTubeService youtube, List<string> videoIds)
    {
        if (videoIds.Count == 0) return [];

        // YouTube API allows up to 50 IDs per request
        var results = new List<VideoData>();

        foreach (var batch in videoIds.Chunk(50))
        {
            var request = youtube.Videos.List("snippet,statistics,contentDetails");
            request.Id = string.Join(",", batch);

            var response = await request.ExecuteAsync();

            foreach (var video in response.Items)
            {
                results.Add(MapToVideoData(video));
            }
        }

        return results;
    }

    public async Task<ChannelData?> GetChannelDetailsAsync(string channelId)
    {
        return await ExecuteWithRotationAsync(async youtube =>
        {
            var request = youtube.Channels.List("snippet,statistics,brandingSettings");
            request.Id = channelId;

            var response = await request.ExecuteAsync();
            var channel = response.Items.FirstOrDefault();

            if (channel == null) return null;

            return new ChannelData
            {
                ChannelId = channel.Id,
                Title = channel.Snippet.Title,
                Description = channel.Snippet.Description ?? "",
                CustomUrl = channel.Snippet.CustomUrl,
                SubscriberCount = (long)(channel.Statistics.SubscriberCount ?? 0),
                VideoCount = (long)(channel.Statistics.VideoCount ?? 0),
                ViewCount = (long)(channel.Statistics.ViewCount ?? 0),
                Keywords = ParseChannelKeywords(channel.BrandingSettings?.Channel?.Keywords),
                Thumbnails = new ThumbnailSet
                {
                    Default = channel.Snippet.Thumbnails?.Default__?.Url,
                    Medium = channel.Snippet.Thumbnails?.Medium?.Url,
                    High = channel.Snippet.Thumbnails?.High?.Url
                }
            };
        }, "GetChannelDetails");
    }

    public async Task<List<VideoData>> GetChannelVideosAsync(string channelId, int maxResults = 50)
    {
        return await ExecuteWithRotationAsync(async youtube =>
        {
            var request = youtube.Search.List("snippet");
            request.ChannelId = channelId;
            request.Type = "video";
            request.Order = SearchResource.ListRequest.OrderEnum.Date;
            request.MaxResults = maxResults;

            var response = await request.ExecuteAsync();

            var videoIds = response.Items
                .Where(i => i.Id.VideoId != null)
                .Select(i => i.Id.VideoId!)
                .ToList();

            return await GetVideoDetailsBatchInternalAsync(youtube, videoIds);
        }, "GetChannelVideos");
    }

    public async Task<List<CommentData>> GetVideoCommentsAsync(string videoId, int maxResults = 100)
    {
        try
        {
            return await ExecuteWithRotationAsync(async youtube =>
            {
                var request = youtube.CommentThreads.List("snippet");
                request.VideoId = videoId;
                request.MaxResults = Math.Min(maxResults, 100);
                request.Order = CommentThreadsResource.ListRequest.OrderEnum.Relevance;

                var response = await request.ExecuteAsync();

                return response.Items.Select(thread => new CommentData
                {
                    CommentId = thread.Snippet.TopLevelComment.Id,
                    VideoId = videoId,
                    AuthorName = thread.Snippet.TopLevelComment.Snippet.AuthorDisplayName,
                    Text = thread.Snippet.TopLevelComment.Snippet.TextDisplay,
                    LikeCount = (int)(thread.Snippet.TopLevelComment.Snippet.LikeCount ?? 0),
                    PublishedAt = thread.Snippet.TopLevelComment.Snippet.PublishedAtDateTimeOffset?.DateTime ?? DateTime.UtcNow,
                    ReplyCount = (int)(thread.Snippet.TotalReplyCount ?? 0)
                }).ToList();
            }, "GetVideoComments");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get comments for video {VideoId}", videoId);
            return [];
        }
    }

    public async Task<List<VideoData>> GetTrendingVideosAsync(string regionCode = "US", string? categoryId = null)
    {
        return await ExecuteWithRotationAsync(async youtube =>
        {
            var request = youtube.Videos.List("snippet,statistics,contentDetails");
            request.Chart = VideosResource.ListRequest.ChartEnum.MostPopular;
            request.RegionCode = regionCode;
            request.MaxResults = 50;

            if (categoryId != null)
                request.VideoCategoryId = categoryId;

            var response = await request.ExecuteAsync();

            return response.Items.Select(MapToVideoData).ToList();
        }, "GetTrendingVideos");
    }

    private static VideoData MapToVideoData(Google.Apis.YouTube.v3.Data.Video video)
    {
        return new VideoData
        {
            VideoId = video.Id,
            Title = video.Snippet.Title,
            Description = video.Snippet.Description ?? "",
            PublishedAt = video.Snippet.PublishedAtDateTimeOffset?.DateTime ?? DateTime.UtcNow,
            ChannelId = video.Snippet.ChannelId,
            ChannelTitle = video.Snippet.ChannelTitle,
            Tags = video.Snippet.Tags?.ToList() ?? [],
            CategoryId = video.Snippet.CategoryId,
            ViewCount = (long)(video.Statistics?.ViewCount ?? 0),
            LikeCount = (long)(video.Statistics?.LikeCount ?? 0),
            CommentCount = (long)(video.Statistics?.CommentCount ?? 0),
            Duration = ParseDuration(video.ContentDetails?.Duration),
            Definition = video.ContentDetails?.Definition ?? "hd",
            Thumbnails = new ThumbnailSet
            {
                Default = video.Snippet.Thumbnails?.Default__?.Url,
                Medium = video.Snippet.Thumbnails?.Medium?.Url,
                High = video.Snippet.Thumbnails?.High?.Url,
                MaxRes = video.Snippet.Thumbnails?.Maxres?.Url
            }
        };
    }

    private static TimeSpan ParseDuration(string? isoDuration)
    {
        if (string.IsNullOrEmpty(isoDuration)) return TimeSpan.Zero;

        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(isoDuration);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static List<string> ParseChannelKeywords(string? keywords)
    {
        if (string.IsNullOrEmpty(keywords)) return [];

        // Keywords can be space-separated or quoted strings
        return keywords
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim('"', '\''))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();
    }
}
