namespace Liza.Orleans.Grains;

using Liza.Core.Models;
using Liza.Core.Services;
using Liza.Orleans.Grains.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Grain that enriches a single video with all available data
/// Runs transcript and comments extraction in parallel for speed
/// </summary>
public class VideoEnrichmentGrain : Grain, IVideoEnrichmentGrain
{
    private readonly IYouTubeService _youtube;
    private readonly ITranscriptService _transcript;
    private readonly ILogger<VideoEnrichmentGrain> _logger;
    
    private EnrichedVideo? _cachedResult;

    public VideoEnrichmentGrain(
        IYouTubeService youtube,
        ITranscriptService transcript,
        ILogger<VideoEnrichmentGrain> logger)
    {
        _youtube = youtube;
        _transcript = transcript;
        _logger = logger;
    }

    /// <summary>
    /// Full enrichment with transcript (slower, ~2-3s per video)
    /// </summary>
    public async Task<EnrichedVideo> EnrichAsync()
    {
        var videoId = this.GetPrimaryKeyString();
        
        // Return cached if available (videos don't change often)
        if (_cachedResult != null)
        {
            return _cachedResult;
        }
        
        _logger.LogInformation("Enriching video: {VideoId}", videoId);
        
        // Run all extractions in parallel
        var detailsTask = _youtube.GetVideoDetailsAsync(videoId);
        var transcriptTask = _transcript.GetTranscriptAsync(videoId);
        var commentsTask = _youtube.GetVideoCommentsAsync(videoId, 50);
        
        await Task.WhenAll(detailsTask, transcriptTask, commentsTask);
        
        var details = await detailsTask;
        
        if (details == null)
        {
            _logger.LogWarning("Video not found: {VideoId}", videoId);
            throw new KeyNotFoundException($"Video not found: {videoId}");
        }
        
        // Get channel details (optional enrichment)
        ChannelData? channel = null;
        try
        {
            channel = await _youtube.GetChannelDetailsAsync(details.ChannelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get channel for video: {VideoId}", videoId);
        }
        
        _cachedResult = new EnrichedVideo
        {
            Details = details,
            Transcript = await transcriptTask,
            Comments = await commentsTask,
            Channel = channel
        };
        
        _logger.LogInformation(
            "Enriched video {VideoId}: {CommentCount} comments, transcript: {HasTranscript}",
            videoId, 
            _cachedResult.Comments.Count,
            _cachedResult.Transcript != null);
        
        return _cachedResult;
    }

    /// <summary>
    /// Fast enrichment WITHOUT transcript (for speed, ~500ms per video)
    /// Use this for initial results, then optionally backfill transcripts
    /// </summary>
    public async Task<EnrichedVideo> EnrichFastAsync()
    {
        var videoId = this.GetPrimaryKeyString();
        
        // Return cached if available
        if (_cachedResult != null)
        {
            return _cachedResult;
        }
        
        _logger.LogDebug("Fast enriching video: {VideoId}", videoId);
        
        // Only get details + comments (skip slow transcript)
        var detailsTask = _youtube.GetVideoDetailsAsync(videoId);
        var commentsTask = _youtube.GetVideoCommentsAsync(videoId, 20); // Fewer comments for speed
        
        await Task.WhenAll(detailsTask, commentsTask);
        
        var details = await detailsTask;
        
        if (details == null)
        {
            throw new KeyNotFoundException($"Video not found: {videoId}");
        }
        
        // Get channel in parallel with minimal delay  
        ChannelData? channel = null;
        try
        {
            channel = await _youtube.GetChannelDetailsAsync(details.ChannelId);
        }
        catch
        {
            // Ignore channel errors for speed
        }
        
        _cachedResult = new EnrichedVideo
        {
            Details = details,
            Transcript = null, // Skip for speed
            Comments = await commentsTask,
            Channel = channel
        };
        
        return _cachedResult;
    }
}

/// <summary>
/// Grain that gets channel details and recent videos
/// </summary>
public class ChannelGrain : Grain, IChannelGrain
{
    private readonly IYouTubeService _youtube;
    private readonly ILogger<ChannelGrain> _logger;
    
    private ChannelData? _cachedDetails;
    private List<VideoData>? _cachedVideos;
    private DateTime _cacheTime;

    public ChannelGrain(
        IYouTubeService youtube,
        ILogger<ChannelGrain> logger)
    {
        _youtube = youtube;
        _logger = logger;
    }

    public async Task<ChannelData> GetDetailsAsync()
    {
        var channelId = this.GetPrimaryKeyString();
        
        if (_cachedDetails != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromHours(24))
        {
            return _cachedDetails;
        }
        
        _logger.LogDebug("Getting channel details: {ChannelId}", channelId);
        
        var channel = await _youtube.GetChannelDetailsAsync(channelId);
        
        if (channel == null)
        {
            throw new KeyNotFoundException($"Channel not found: {channelId}");
        }
        
        _cachedDetails = channel;
        _cacheTime = DateTime.UtcNow;
        
        return _cachedDetails;
    }

    public async Task<List<VideoData>> GetRecentVideosAsync(int count = 50)
    {
        var channelId = this.GetPrimaryKeyString();
        
        if (_cachedVideos != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromHours(6))
        {
            return _cachedVideos.Take(count).ToList();
        }
        
        _logger.LogDebug("Getting recent videos for channel: {ChannelId}", channelId);
        
        _cachedVideos = await _youtube.GetChannelVideosAsync(channelId, count);
        _cacheTime = DateTime.UtcNow;
        
        return _cachedVideos;
    }
}

/// <summary>
/// Grain that extracts transcript from a video
/// </summary>
public class TranscriptGrain : Grain, ITranscriptGrain
{
    private readonly ITranscriptService _transcript;
    private readonly ILogger<TranscriptGrain> _logger;
    
    private TranscriptData? _cachedTranscript;

    public TranscriptGrain(
        ITranscriptService transcript,
        ILogger<TranscriptGrain> logger)
    {
        _transcript = transcript;
        _logger = logger;
    }

    public async Task<TranscriptData?> GetTranscriptAsync()
    {
        var videoId = this.GetPrimaryKeyString();
        
        // Transcripts never change, cache forever
        if (_cachedTranscript != null)
        {
            return _cachedTranscript;
        }
        
        _logger.LogDebug("Getting transcript for video: {VideoId}", videoId);
        
        _cachedTranscript = await _transcript.GetTranscriptAsync(videoId);
        
        return _cachedTranscript;
    }
}

/// <summary>
/// Grain that gets comments from a video
/// </summary>
public class CommentsGrain : Grain, ICommentsGrain
{
    private readonly IYouTubeService _youtube;
    private readonly ILogger<CommentsGrain> _logger;
    
    private List<CommentData>? _cachedComments;
    private DateTime _cacheTime;

    public CommentsGrain(
        IYouTubeService youtube,
        ILogger<CommentsGrain> logger)
    {
        _youtube = youtube;
        _logger = logger;
    }

    public async Task<List<CommentData>> GetCommentsAsync(int limit = 100)
    {
        var videoId = this.GetPrimaryKeyString();
        
        // Cache for 6 hours
        if (_cachedComments != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromHours(6))
        {
            return _cachedComments.Take(limit).ToList();
        }
        
        _logger.LogDebug("Getting comments for video: {VideoId}", videoId);
        
        _cachedComments = await _youtube.GetVideoCommentsAsync(videoId, limit);
        _cacheTime = DateTime.UtcNow;
        
        return _cachedComments;
    }
}
