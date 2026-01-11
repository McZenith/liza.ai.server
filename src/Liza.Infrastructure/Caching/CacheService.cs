namespace Liza.Infrastructure.Caching;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

/// <summary>
/// Redis-based caching service for extraction results
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null);
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);
}

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (value.IsNullOrEmpty)
                return default;
            
            string jsonString = value.ToString();
            return JsonSerializer.Deserialize<T>(jsonString, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cache key: {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            await _db.StringSetAsync(key, json, expiry ?? TimeSpan.FromHours(6));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set cache key: {Key}", key);
        }
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
    {
        var cached = await GetAsync<T>(key);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return cached;
        }
        
        _logger.LogDebug("Cache miss: {Key}", key);
        var value = await factory();
        await SetAsync(key, value, expiry);
        return value;
    }

    public async Task RemoveAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await _db.KeyExistsAsync(key);
    }
}

/// <summary>
/// Cache key builder for consistent key naming
/// </summary>
public static class CacheKeys
{
    public static string Video(string videoId) => $"video:{videoId}";
    public static string Channel(string channelId) => $"channel:{channelId}";
    public static string Search(string query, int page = 1) => $"search:{query}:{page}";
    public static string Keyword(string keyword) => $"keyword:{keyword}";
    public static string Trends(string keyword, string region) => $"trends:{region}:{keyword}";
    public static string Autocomplete(string source, string query) => $"autocomplete:{source}:{query}";
    public static string Transcript(string videoId) => $"transcript:{videoId}";
    public static string Comments(string videoId) => $"comments:{videoId}";
    public static string GoogleVideo(string query) => $"googlevideo:{query}";
}

/// <summary>
/// Cache TTL configurations
/// </summary>
public static class CacheTtl
{
    public static readonly TimeSpan VideoDetails = TimeSpan.FromHours(24);
    public static readonly TimeSpan ChannelDetails = TimeSpan.FromHours(24);
    public static readonly TimeSpan Comments = TimeSpan.FromHours(6);
    public static readonly TimeSpan Transcript = TimeSpan.FromDays(7);
    public static readonly TimeSpan KeywordVolume = TimeSpan.FromDays(7);
    public static readonly TimeSpan Trends = TimeSpan.FromHours(1);
    public static readonly TimeSpan SearchResults = TimeSpan.FromHours(6);
    public static readonly TimeSpan Autocomplete = TimeSpan.FromHours(12);
    public static readonly TimeSpan GoogleVideo = TimeSpan.FromHours(6);
}
