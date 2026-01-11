namespace Liza.Silo;

using Liza.Orleans.Grains.Abstractions;

public class Worker(ILogger<Worker> logger, IGrainFactory grainFactory) : BackgroundService
{
    // Default region to warm up (US is most common)
    private const string DefaultRegion = "US";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Trending Warmup Worker started at: {Time}", DateTimeOffset.Now);

        // Run warmup immediately on startup (for fresh deployments)
        await RunWarmupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Calculate delay until next 6 AM UTC
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddDays(1).AddHours(6); // 6 AM UTC tomorrow
            
            // If it's before 6 AM today, run today instead
            if (now.Hour < 6)
            {
                nextRun = now.Date.AddHours(6);
            }
            
            var delay = nextRun - now;
            logger.LogInformation("Next trending warmup scheduled at: {NextRun} (in {Hours:F1} hours)", 
                nextRun, delay.TotalHours);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await RunWarmupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Trending Warmup Worker stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during trending warmup cycle");
                // Wait 1 hour before retrying on error
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task RunWarmupAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting trending warmup for default region: {Region}", DefaultRegion);
        var startTime = DateTime.UtcNow;

        try
        {
            var grain = grainFactory.GetGrain<ITrendingAnalysisGrain>(DefaultRegion);
            await grain.WarmupAsync();
            logger.LogInformation("Completed warmup for region: {Region}", DefaultRegion);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to warm up region: {Region}", DefaultRegion);
        }

        var elapsed = DateTime.UtcNow - startTime;
        logger.LogInformation("Trending warmup completed in {Elapsed:F1} seconds", elapsed.TotalSeconds);
    }
}
