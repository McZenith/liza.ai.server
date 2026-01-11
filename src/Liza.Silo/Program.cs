using Liza.Infrastructure;
using Liza.Orleans.Grains;
using Liza.Silo;

var builder = Host.CreateApplicationBuilder(args);

// Get MongoDB connection string
var mongoConnectionString = builder.Configuration["MONGODB_URI"] 
    ?? Environment.GetEnvironmentVariable("MONGODB_URI") 
    ?? "mongodb://localhost:27017";

// Add Orleans
builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering() // For local development
        .AddMemoryGrainStorage("Default")
        .UseMongoDBClient(mongoConnectionString) // Registers IMongoClientFactory
        .AddMongoDBGrainStorage("KeywordCache", options =>
        {
            options.DatabaseName = "liza";
            options.CollectionPrefix = "cache_";
        })
        .ConfigureLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
});

// Add infrastructure services (YouTube, Autocomplete, etc.)
builder.Services.AddLizaInfrastructure(builder.Configuration);

// Add trending warmup worker (runs on startup and daily at 6 AM UTC)
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

Console.WriteLine("Starting Liza Orleans Silo...");
host.Run();
