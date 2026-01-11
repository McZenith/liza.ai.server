using Liza.Api.GraphQL;
using Liza.Api.GraphQL.Types;
using Liza.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add Orleans Client (connects to Silo)
builder.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseLocalhostClustering();
});

// Add Infrastructure services
builder.Services.AddLizaInfrastructure(builder.Configuration);

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
        policy.WithOrigins("http://localhost:3000")
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

Console.WriteLine("Liza API starting...");
Console.WriteLine("GraphQL: http://localhost:5000/graphql");

app.Run();

