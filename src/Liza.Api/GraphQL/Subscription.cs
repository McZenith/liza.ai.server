namespace Liza.Api.GraphQL;

using HotChocolate;
using HotChocolate.Subscriptions;
using Liza.Core.Models;
using Liza.Orleans.Grains.Abstractions;

/// <summary>
/// GraphQL Subscription root - real-time updates
/// </summary>
public class Subscription
{
    /// <summary>
    /// Subscribe to long-tail keyword analysis updates
    /// Receives results as each long-tail variation is analyzed
    /// </summary>
    [Subscribe]
    [Topic("{parentKeyword}")]
    public LongTailAnalysisUpdate OnLongTailAnalyzed(
        [EventMessage] LongTailAnalysisUpdate update,
        string parentKeyword) => update;
}

/// <summary>
/// Update sent when a long-tail keyword analysis completes
/// </summary>
public class LongTailAnalysisUpdate
{
    public required string ParentKeyword { get; init; }
    public required string LongTailKeyword { get; init; }
    public int Opportunity { get; init; }
    public int Difficulty { get; init; }
    public required string Grade { get; init; }
    public long SearchVolume { get; init; }
    public required string CompetitionLevel { get; init; }
    public int VideoCount { get; init; }
    public long AvgCompetitorViews { get; init; }
    public DateTime AnalyzedAt { get; init; } = DateTime.UtcNow;
    public required string Source { get; init; }  // YouTube/Google/Extracted
    public bool IsComplete { get; init; }  // True when this is the last update
    public int AnalyzedCount { get; init; }  // How many have been analyzed so far
    public int TotalCount { get; init; }  // Total to be analyzed
    
    /// <summary>
    /// Cumulative list of ALL results so far (for frontend to show full list)
    /// </summary>
    public List<LongTailResultSummary> AllResults { get; init; } = [];
}

/// <summary>
/// Lightweight summary of a long-tail result for cumulative list
/// </summary>
public class LongTailResultSummary
{
    public required string Keyword { get; init; }
    public required string Grade { get; init; }
    public int Opportunity { get; init; }
    public int Difficulty { get; init; }
    public long SearchVolume { get; init; }
    public required string Source { get; init; }
}
