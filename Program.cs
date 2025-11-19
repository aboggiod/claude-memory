using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Options;
using ClaudeMemoryApi.Models;
using ClaudeMemoryApi.Storage;
using ClaudeMemoryApi.Services;
using ClaudeMemoryApi.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Claude Memory API",
        Version = "v1",
        Description = "Production-grade persistent memory storage for Claude conversations with advanced indexing, deduplication, lifecycle management, and compression."
    });
});

builder.Services.AddCors();

// ============================================================================
// Configuration
// ============================================================================

builder.Services.Configure<MemoryOptions>(
    builder.Configuration.GetSection("Memory"));

// ============================================================================
// Storage and Services Registration
// ============================================================================

builder.Services.AddSingleton<IMemoryStore>(sp =>
{
    var options = sp.GetRequiredService<IOptions<MemoryOptions>>().Value;

    if (options.StorageEngine.Equals("wal", StringComparison.OrdinalIgnoreCase))
    {
        // Advanced WAL-based storage
        return new WalMemoryStore(
            options.DataDirectory,
            options.WalMaxOperationsBeforeCompaction,
            options.WalMaxBytesBeforeCompaction);
    }
    else
    {
        // Simple single-file storage (backward compatible)
        var filePath = options.GetMainFilePath();
        return new JsonLinesMemoryStore(filePath);
    }
});

builder.Services.AddSingleton<IInternalMemoryStore>(sp =>
    sp.GetRequiredService<IMemoryStore>() as IInternalMemoryStore
    ?? throw new InvalidOperationException("Storage must implement IInternalMemoryStore"));

// Services
builder.Services.AddSingleton<IMemoryQueryService, MemoryQueryService>();
builder.Services.AddSingleton<IMemoryLifecycleService, MemoryLifecycleService>();

builder.Services.AddSingleton<IDeduplicationService>(sp =>
{
    var options = sp.GetRequiredService<IOptions<MemoryOptions>>().Value;
    var store = sp.GetRequiredService<IInternalMemoryStore>();

    var dedupOptions = new DeduplicationOptions
    {
        EnableDeduplication = options.EnableDeduplication,
        DeduplicationMode = options.DeduplicationMode,
        DeduplicationThreshold = options.DeduplicationThreshold
    };

    return new DeduplicationService(store, dedupOptions);
});

builder.Services.AddSingleton<ICompressionService, CompressionService>();

var app = builder.Build();

/*
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost - breaking routing
});
*/

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.UseSwagger();
app.UseSwagger(c =>
{
    c.PreSerializeFilters.Add((swagger, httpReq) =>
    {
        swagger.Servers = new List<OpenApiServer>
        {
            new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" }
        };
    });
});
app.UseSwaggerUI();

// Add HEAD request support
app.Use(async (context, next) =>
{
    if (context.Request.Method == "HEAD")
    {
        context.Request.Method = "GET";
    }
    await next();
});

// ============================================================================
// ENDPOINTS
// ============================================================================

app.MapGet("/health", () =>
    Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }))
    .WithName("HealthCheck")
    .WithDescription("Verify API is reachable and functional")
    .WithTags("memory-api")
    .Produces<object>(200);

app.MapGet("/", () =>
    Results.Ok(new
    {
        name = "Claude Memory API",
        version = "v1",
        status = "operational",
        swagger = "/swagger"
    }))
    .WithTags("memory-api")
    .Produces<object>(200);

app.MapPost("/memory", async (
    [FromBody] CreateMemoryRequest req,
    IInternalMemoryStore store,
    IDeduplicationService? dedupService) =>
{
    if (string.IsNullOrWhiteSpace(req.Role) || string.IsNullOrWhiteSpace(req.Content))
        return Results.BadRequest(new { error = "role and content required" });

    var internalRecord = new InternalMemoryRecord(
        Id: Guid.NewGuid().ToString("N"),
        Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Role: req.Role.ToLowerInvariant(),
        Content: req.Content,
        Tags: req.Tags ?? new List<string>(),
        Context: req.Context
    );

    // Apply deduplication if service is available
    InternalMemoryRecord finalRecord;
    if (dedupService != null)
    {
        finalRecord = await dedupService.HandleDeduplicationAsync(internalRecord);
    }
    else
    {
        finalRecord = await store.AddInternalAsync(internalRecord);
    }

    return Results.Ok(new { status = "ok", id = finalRecord.Id, timestamp = finalRecord.Timestamp });
})
.WithName("CreateMemory")
.WithDescription("Store a new memory with role, content, tags, and optional context. Automatic deduplication if enabled.")
.WithTags("memory-api")
.Accepts<CreateMemoryRequest>("application/json")
.Produces<object>(200)
.Produces<object>(400);

app.MapGet("/memory", async (
    [FromQuery] long? since,
    [FromQuery] long? before,
    [FromQuery] string? role,
    [FromQuery] string? tags,
    [FromQuery] string? context,
    [FromQuery] string? content,
    [FromQuery] int limit,
    [FromQuery] bool includeExpired,
    IMemoryQueryService queryService) =>
{
    // Preserve default limit behavior
    if (limit == 0) limit = 100;

    var filtered = await queryService.QueryAsync(
        sinceTimestamp: since,
        beforeTimestamp: before,
        role: role,
        tags: tags,
        context: context,
        content: content,
        limit: limit,
        includeExpired: includeExpired
    );

    return Results.Ok(filtered);
})
.WithName("GetMemories")
.WithDescription("Retrieve memories with optional filters: since, before, role, tags, context, content keywords, limit, and includeExpired")
.WithTags("memory-api")
.Produces<List<MemoryRecord>>(200);

app.MapPut("/memory/{id}", async (string id, [FromBody] UpdateMemoryRequest req, IMemoryStore store) =>
{
    var existingRecord = await store.GetByIdAsync(id);

    if (existingRecord == null)
        return Results.NotFound(new { error = "memory not found" });

    var updated = existingRecord with
    {
        Role = req.Role ?? existingRecord.Role,
        Content = req.Content ?? existingRecord.Content,
        Tags = req.Tags ?? existingRecord.Tags,
        Context = req.Context ?? existingRecord.Context
    };

    await store.UpdateAsync(id, updated);

    return Results.Ok(new { status = "ok", id });
})
.WithName("UpdateMemory")
.WithDescription("Update an existing memory by ID (partial updates supported)")
.WithTags("memory-api")
.Accepts<UpdateMemoryRequest>("application/json")
.Produces<object>(200)
.Produces<object>(404);

app.MapDelete("/memory/{id}", async (string id, IMemoryStore store) =>
{
    await store.DeleteAsync(id);
    return Results.Ok(new { status = "ok" });
})
.WithName("DeleteMemory")
.WithDescription("Delete a specific memory by ID")
.WithTags("memory-api")
.Produces<object>(200);

app.MapDelete("/memory", async (
    [FromQuery] long? before,
    [FromQuery] string? tags,
    [FromQuery] string? role,
    IMemoryStore store) =>
{
    if (!before.HasValue && string.IsNullOrWhiteSpace(tags) && string.IsNullOrWhiteSpace(role))
        return Results.BadRequest(new { error = "provide at least one filter: before, tags, or role" });

    var tagSet = string.IsNullOrWhiteSpace(tags)
        ? new HashSet<string>()
        : tags.Split(',').Select(t => t.Trim().ToLowerInvariant()).ToHashSet();

    // Delete records that match the filter criteria
    await store.DeleteManyAsync(r =>
    {
        if (before.HasValue && r.Timestamp >= before.Value) return true;
        if (tagSet.Count > 0 && r.Tags.Any(t => tagSet.Contains(t.ToLowerInvariant()))) return true;
        if (!string.IsNullOrWhiteSpace(role) && r.Role == role.ToLowerInvariant()) return true;
        return false;
    });

    return Results.Ok(new { status = "ok" });
})
.WithName("BulkDeleteMemories")
.WithDescription("Bulk delete memories by filters: before timestamp, tags, or role")
.WithTags("memory-api")
.Produces<object>(200)
.Produces<object>(400);

app.MapDelete("/memory/all", async (IMemoryStore store) =>
{
    await store.DeleteAllAsync();
    return Results.Ok(new { status = "ok" });
})
.WithName("DeleteAllMemories")
.WithDescription("Nuclear option: delete all memories")
.WithTags("memory-api")
.Produces<object>(200);

// ============================================================================
// NEW ENDPOINT: Compression/Summarization
// ============================================================================

app.MapPost("/memory/compress", async (
    [FromBody] CompressMemoriesRequest req,
    ICompressionService compressionService) =>
{
    try
    {
        var summary = await compressionService.CompressMemoriesAsync(req);
        return Results.Ok(summary);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("CompressMemories")
.WithDescription("Compress/summarize multiple memories into a single summary memory. Uses deterministic heuristics (no external LLM).")
.WithTags("memory-api")
.Accepts<CompressMemoriesRequest>("application/json")
.Produces<MemoryRecord>(200)
.Produces<object>(400)
.Produces<object>(404);

app.Urls.Add("http://0.0.0.0:5000");
app.Run();
