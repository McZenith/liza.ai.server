# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Liza.sln ./
COPY src/Liza.Core/Liza.Core.csproj src/Liza.Core/
COPY src/Liza.Infrastructure/Liza.Infrastructure.csproj src/Liza.Infrastructure/
COPY src/Liza.Orleans/Liza.Orleans.csproj src/Liza.Orleans/
COPY src/Liza.Silo/Liza.Silo.csproj src/Liza.Silo/

# Restore dependencies
RUN dotnet restore

# Copy everything else
COPY . .

# Build and publish
RUN dotnet publish src/Liza.Silo/Liza.Silo.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Heroku uses PORT env variable
ENV ASPNETCORE_URLS=http://+:${PORT:-5000}

# Start the app
ENTRYPOINT ["dotnet", "Liza.Silo.dll"]
