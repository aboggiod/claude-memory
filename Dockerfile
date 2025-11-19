# Multi-stage Dockerfile for Claude Memory API
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Copy project file and restore dependencies
COPY ClaudeMemoryApi.csproj .
RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create data directory
RUN mkdir -p /data

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV Memory__DataDirectory=/data

# Expose port
EXPOSE 5000

# Run the application
ENTRYPOINT ["dotnet", "ClaudeMemoryApi.dll"]
