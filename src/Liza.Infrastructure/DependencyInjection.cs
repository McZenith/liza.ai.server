namespace Liza.Infrastructure;

using Liza.Core.Services;
using Liza.Infrastructure.Autocomplete;
using Liza.Infrastructure.Caching;
using Liza.Infrastructure.Keywords;
using Liza.Infrastructure.Reddit;
using Liza.Infrastructure.Search;
using Liza.Infrastructure.Trends;
using Liza.Infrastructure.YouTube;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

public static class DependencyInjection
{
    public static IServiceCollection AddLizaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Redis
        var redisConnection = configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => 
            ConnectionMultiplexer.Connect(redisConnection));
        services.AddScoped<ICacheService, RedisCacheService>();

        // HTTP clients for scraping services
        services.AddHttpClient<IAutocompleteService, AutocompleteService>();
        services.AddHttpClient<IGoogleTrendsService, GoogleTrendsService>();
        services.AddHttpClient<IRedditService, RedditService>();
        services.AddHttpClient<GoogleVideoSearchService>();
        services.AddScoped<IGoogleVideoSearchService>(sp => 
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(GoogleVideoSearchService));
            var cache = sp.GetRequiredService<ICacheService>();
            var logger = sp.GetRequiredService<ILogger<GoogleVideoSearchService>>();
            return new GoogleVideoSearchService(httpClient, cache, logger);
        });
        
        // Transcript service with HTTP extraction
        services.AddScoped<ITranscriptService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<TranscriptService>>();
            return new TranscriptService(logger, httpClientFactory);
        });

        // YouTube API
        services.Configure<YouTubeServiceOptions>(configuration.GetSection("YouTube"));
        services.AddScoped<IYouTubeService, YouTubeDataService>();
        
        // Google Ads Keyword Planner
        services.Configure<GoogleAdsOptions>(configuration.GetSection("GoogleAds"));
        services.AddScoped<IKeywordPlannerService, KeywordPlannerService>();

        // Analysis Services
        services.AddScoped<Analysis.ISearchDemandService, Analysis.SearchDemandService>();
        services.AddScoped<Analysis.IContentGapService, Analysis.ContentGapService>();
        services.AddScoped<Analysis.IRankingFactorService, Analysis.RankingFactorService>();
        services.AddScoped<Analysis.IKeywordScoreService, Analysis.KeywordScoreService>();
        services.AddScoped<Analysis.IKeywordExtractionService, Analysis.KeywordExtractionService>();
        services.AddScoped<Analysis.IRecommendationOptimizationService, Analysis.RecommendationOptimizationService>();

        return services;
    }
}
