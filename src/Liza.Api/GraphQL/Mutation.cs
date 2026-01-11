namespace Liza.Api.GraphQL;

using HotChocolate.Subscriptions;
using Liza.Core.Models;
using Liza.Orleans.Grains.Abstractions;

/// <summary>
/// GraphQL Mutation root - write operations
/// </summary>
public class Mutation
{
    /// <summary>
    /// Start deep analysis of top long-tail variations
    /// Subscribe to OnLongTailAnalyzed with the parentKeyword to receive updates
    /// </summary>
    [GraphQLDescription("Start parallel analysis of top 10 long-tail variations. Subscribe to onLongTailAnalyzed to receive results.")]
    public async Task<LongTailAnalysisStarted> StartLongTailAnalysis(
        [GraphQLDescription("The parent keyword to analyze long-tails for")] string keyword,
        [GraphQLDescription("Max number of long-tails to analyze (default 10)")] int? maxVariations,
        [Service] IGrainFactory grainFactory,
        [Service] ITopicEventSender eventSender)
    {
        var grain = grainFactory.GetGrain<IKeywordAnalysisGrain>(keyword);
        var max = maxVariations ?? 10;
        
        // Kick off the analysis in background and publish updates
        _ = Task.Run(async () =>
        {
            try
            {
                var results = await grain.AnalyzeLongTailsAsync(max);
                var total = results.Count;
                var count = 0;
                
                foreach (var result in results)
                {
                    count++;
                    var update = new LongTailAnalysisUpdate
                    {
                        ParentKeyword = keyword,
                        LongTailKeyword = result.LongTailKeyword,
                        Opportunity = result.Opportunity,
                        Difficulty = result.Difficulty,
                        Grade = result.Grade,
                        SearchVolume = result.SearchVolume,
                        CompetitionLevel = result.CompetitionLevel,
                        VideoCount = result.VideoCount,
                        AvgCompetitorViews = result.AvgCompetitorViews,
                        AnalyzedAt = result.AnalyzedAt,
                        Source = result.Source,
                        IsComplete = count == total,
                        AnalyzedCount = count,
                        TotalCount = total
                    };
                    
                    await eventSender.SendAsync(keyword, update);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing long-tails: {ex.Message}");
            }
        });
        
        return new LongTailAnalysisStarted
        {
            Keyword = keyword,
            MaxVariations = max,
            StartedAt = DateTime.UtcNow,
            SubscriptionTopic = keyword
        };
    }
}

/// <summary>
/// Response when long-tail analysis is started
/// </summary>
public class LongTailAnalysisStarted
{
    public required string Keyword { get; init; }
    public int MaxVariations { get; init; }
    public DateTime StartedAt { get; init; }
    public required string SubscriptionTopic { get; init; }
}
