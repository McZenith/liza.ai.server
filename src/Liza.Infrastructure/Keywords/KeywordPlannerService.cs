namespace Liza.Infrastructure.Keywords;

using Google.Ads.GoogleAds.Config;
using Google.Ads.GoogleAds.Lib;
using Google.Ads.GoogleAds.V22.Enums;
using Google.Ads.GoogleAds.V22.Resources;
using Google.Ads.GoogleAds.V22.Services;
using Liza.Core.Models;
using Liza.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Google Ads Keyword Planner options
/// </summary>
public class GoogleAdsOptions
{
    public string DeveloperToken { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string LoginCustomerId { get; set; } = "";
}

/// <summary>
/// Google Ads Keyword Planner service for keyword metrics
/// </summary>
public class KeywordPlannerService : IKeywordPlannerService
{
    private readonly GoogleAdsClient _client;
    private readonly string _customerId;
    private readonly ILogger<KeywordPlannerService> _logger;

    public KeywordPlannerService(
        IOptions<GoogleAdsOptions> options,
        ILogger<KeywordPlannerService> logger)
    {
        _logger = logger;
        _customerId = options.Value.CustomerId.Replace("-", "");
        
        var config = new GoogleAdsConfig
        {
            DeveloperToken = options.Value.DeveloperToken,
            OAuth2ClientId = options.Value.ClientId,
            OAuth2ClientSecret = options.Value.ClientSecret,
            OAuth2RefreshToken = options.Value.RefreshToken,
            LoginCustomerId = string.IsNullOrEmpty(options.Value.LoginCustomerId) 
                ? _customerId 
                : options.Value.LoginCustomerId.Replace("-", "")
        };
        
        _client = new GoogleAdsClient(config);
    }

    public async Task<KeywordData> GetKeywordMetricsAsync(string keyword)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Getting keyword metrics for: {Keyword}", keyword);
                
                KeywordPlanIdeaServiceClient keywordPlanIdeaService = 
                    _client.GetService(Google.Ads.GoogleAds.Services.V22.KeywordPlanIdeaService);
                
                var request = new GenerateKeywordIdeasRequest
                {
                    CustomerId = _customerId,
                    Language = "languageConstants/1000", // English
                    GeoTargetConstants = { "geoTargetConstants/2840" }, // US
                    KeywordPlanNetwork = KeywordPlanNetworkEnum.Types.KeywordPlanNetwork.GoogleSearchAndPartners,
                    KeywordSeed = new KeywordSeed { Keywords = { keyword } }
                };
                
                var response = keywordPlanIdeaService.GenerateKeywordIdeas(request);
                
                // Find exact match for the keyword
                foreach (var result in response)
                {
                    if (result.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return MapToKeywordData(result);
                    }
                }
                
                // Return first result as fallback
                foreach (var result in response)
                {
                    return MapToKeywordData(result);
                }
                
                return CreateEmptyKeywordData(keyword);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get keyword metrics for: {Keyword}", keyword);
                return CreateEmptyKeywordData(keyword);
            }
        });
    }

    public async Task<List<KeywordData>> GetKeywordIdeasAsync(string seedKeyword, int limit = 50)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Getting keyword ideas for: {Keyword}", seedKeyword);
                
                KeywordPlanIdeaServiceClient keywordPlanIdeaService = 
                    _client.GetService(Google.Ads.GoogleAds.Services.V22.KeywordPlanIdeaService);
                
                var request = new GenerateKeywordIdeasRequest
                {
                    CustomerId = _customerId,
                    Language = "languageConstants/1000", // English
                    GeoTargetConstants = { "geoTargetConstants/2840" }, // US
                    KeywordPlanNetwork = KeywordPlanNetworkEnum.Types.KeywordPlanNetwork.GoogleSearchAndPartners,
                    KeywordSeed = new KeywordSeed { Keywords = { seedKeyword } }
                };
                
                var response = keywordPlanIdeaService.GenerateKeywordIdeas(request);
                
                var results = new List<KeywordData>();
                
                foreach (var result in response)
                {
                    if (results.Count >= limit) break;
                    results.Add(MapToKeywordData(result));
                }
                
                _logger.LogInformation("Found {Count} keyword ideas for: {Keyword}", results.Count, seedKeyword);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get keyword ideas for: {Keyword}", seedKeyword);
                return new List<KeywordData>();
            }
        });
    }

    private static KeywordData MapToKeywordData(GenerateKeywordIdeaResult result)
    {
        var metrics = result.KeywordIdeaMetrics;
        
        // Map competition level
        var competitionLevel = metrics?.Competition switch
        {
            KeywordPlanCompetitionLevelEnum.Types.KeywordPlanCompetitionLevel.Low => "low",
            KeywordPlanCompetitionLevelEnum.Types.KeywordPlanCompetitionLevel.Medium => "medium",
            KeywordPlanCompetitionLevelEnum.Types.KeywordPlanCompetitionLevel.High => "high",
            _ => "unknown"
        };
        
        return new KeywordData
        {
            Keyword = result.Text,
            SearchVolume = (int)(metrics?.AvgMonthlySearches ?? 0),
            Competition = competitionLevel,
            CompetitionIndex = (int)(metrics?.CompetitionIndex ?? 0),
            LowBidCents = (int)((metrics?.LowTopOfPageBidMicros ?? 0) / 10000),
            HighBidCents = (int)((metrics?.HighTopOfPageBidMicros ?? 0) / 10000)
        };
    }

    private static KeywordData CreateEmptyKeywordData(string keyword) => new()
    {
        Keyword = keyword,
        SearchVolume = 0,
        Competition = "unknown",
        CompetitionIndex = 0,
        LowBidCents = 0,
        HighBidCents = 0
    };
}
