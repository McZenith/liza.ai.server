using Liza.Api.GraphQL;
using Liza.Api.GraphQL.Types;
using Liza.Infrastructure;
using Liza.Orleans.Grains;
using Liza.Silo;

var builder = WebApplication.CreateBuilder(args);

// Get PORT from environment (Heroku sets this)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://*:{port}");

// Get MongoDB connection string
var mongoConnectionString = builder.Configuration["MongoDb:ConnectionString"] 
    ?? builder.Configuration["MONGODB_URI"] 
    ?? Environment.GetEnvironmentVariable("MONGODB_URI") 
    ?? Environment.GetEnvironmentVariable("MongoDb__ConnectionString")
    ?? "mongodb://localhost:27017";

Console.WriteLine($"Starting Liza Server on port {port}...");
Console.WriteLine($"MongoDB: {(mongoConnectionString.Contains("mongodb+srv") ? "Atlas" : "Local")}");

// Add Orleans Silo (hosts grains directly - no separate client needed)
builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering() // Single-node deployment
        .AddMemoryGrainStorage("Default")
        .UseMongoDBClient(mongoConnectionString)
        .AddMongoDBGrainStorage("KeywordCache", options =>
        {
            options.DatabaseName = builder.Configuration["MongoDb:DatabaseName"] ?? "Liza";
            options.CollectionPrefix = "cache_";
        })
        .ConfigureLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
});

// Add Infrastructure services (YouTube, Autocomplete, etc.)
builder.Services.AddLizaInfrastructure(builder.Configuration);

// Add trending warmup worker
builder.Services.AddHostedService<Worker>();

// Add GraphQL with Subscriptions
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddSubscriptionType<Subscription>()
    .AddType<VideoType>()
    .AddType<EnrichedVideoType>()
    .AddType<TranscriptType>()
    .AddType<CommentType>()
    .AddType<ChannelType>()
    .AddType<KeywordResearchResultType>()
    .AddType<TrendDataType>()
    .AddInMemorySubscriptions()
    .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = builder.Environment.IsDevelopment());

// CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

// GraphQL endpoint
app.MapGraphQL();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
app.MapGet("/", () => Results.Redirect("/graphql"));

Console.WriteLine($"Liza Server started!");
Console.WriteLine($"GraphQL: http://localhost:{port}/graphql");
Console.WriteLine($"Health: http://localhost:{port}/health");

app.Run();
