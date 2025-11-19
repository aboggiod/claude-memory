using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using Microsoft.AspNetCore.HttpOverrides;
using OpenApi.Models;
using OpenApi.Servers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "Claude Memory API", 
        Version = "v1",
        Description = "Persistent memory storage for Claude conversations with tagging, context, and full CRUD operations."
    });
});

builder.Services.AddCors();

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
        swagger.Servers = new List<Microsoft.OpenApi.Models.OpenApiServer>
        {
            new Microsoft.OpenApi.Models.OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" }
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

// File storage setup
string EnsureDir(string path)
{
    var dir = Path.GetDirectoryName(path) 
        ?? throw new InvalidOperationException("Invalid path");
    Directory.CreateDirectory(dir);
    return path;
}

var CLAUDE_FILE = EnsureDir("C:\\Temp\\ai-memory\\claude.jsonl");
var fileSemaphore = new SemaphoreSlim(1, 1);

// Helper: Read and deserialize all records
async Task<List<MemoryRecord>> ReadAllRecordsAsync()
{
    if (!File.Exists(CLAUDE_FILE)) return new List<MemoryRecord>();
    
    await fileSemaphore.WaitAsync();
    try
    {
        var records = new List<MemoryRecord>();
        await foreach (var line in File.ReadLinesAsync(CLAUDE_FILE))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<MemoryRecord>(line);
                if (record != null) records.Add(record);
            }
            catch { /* Skip corrupted lines */ }
        }
        return records;
    }
    finally
    {
        fileSemaphore.Release();
    }
}

// Helper: Write records back to file
async Task WriteAllRecordsAsync(IEnumerable<MemoryRecord> records)
{
    var tempPath = CLAUDE_FILE + ".tmp";
    
    await fileSemaphore.WaitAsync();
    try
    {
        await using var writer = new StreamWriter(tempPath, false);
        foreach (var record in records)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(record));
        }
    }
    finally
    {
        fileSemaphore.Release();
    }
    
    File.Replace(tempPath, CLAUDE_FILE, null);
}

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
    Results.Ok(new { 
        name = "Claude Memory API", 
        version = "v1",
        status = "operational",
        swagger = "/swagger"
    }))
    .WithTags("memory-api")
    .Produces<object>(200);

app.MapPost("/memory", async ([FromBody] CreateMemoryRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Role) || string.IsNullOrWhiteSpace(req.Content))
        return Results.BadRequest(new { error = "role and content required" });

    var record = new MemoryRecord(
        Id: Guid.NewGuid().ToString("N"),
        Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Role: req.Role.ToLowerInvariant(),
        Content: req.Content,
        Tags: req.Tags ?? new List<string>(),
        Context: req.Context
    );

    await fileSemaphore.WaitAsync();
    try
    {
        await File.AppendAllTextAsync(CLAUDE_FILE, JsonSerializer.Serialize(record) + "\n");
    }
    finally
    {
        fileSemaphore.Release();
    }

    return Results.Ok(new { status = "ok", id = record.Id, timestamp = record.Timestamp });
})
.WithName("CreateMemory")
.WithDescription("Store a new memory with role, content, tags, and optional context")
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
    [FromQuery] int limit = 100) =>
{
    var records = await ReadAllRecordsAsync();
    
    var tagSet = string.IsNullOrWhiteSpace(tags)
        ? new HashSet<string>()
        : tags.Split(',').Select(t => t.Trim().ToLowerInvariant()).ToHashSet();

    var filtered = records.Where(r =>
    {
        if (since.HasValue && r.Timestamp <= since.Value) return false;
        if (before.HasValue && r.Timestamp >= before.Value) return false;
        if (!string.IsNullOrWhiteSpace(role) && r.Role != role.ToLowerInvariant()) return false;
        if (tagSet.Count > 0 && !r.Tags.Any(t => tagSet.Contains(t.ToLowerInvariant()))) return false;
        if (!string.IsNullOrWhiteSpace(context) && 
            (r.Context == null || !r.Context.Contains(context, StringComparison.OrdinalIgnoreCase))) return false;
        if (!string.IsNullOrWhiteSpace(content) && 
            !r.Content.Contains(content, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }).Take(limit).ToList();

    return Results.Ok(filtered);
})
.WithName("GetMemories")
.WithDescription("Retrieve memories with optional filters: since, before, role, tags, context, content keywords, and limit")
.WithTags("memory-api")
.Produces<List<MemoryRecord>>(200);

app.MapPut("/memory/{id}", async (string id, [FromBody] UpdateMemoryRequest req) =>
{
    var records = await ReadAllRecordsAsync();
    var record = records.FirstOrDefault(r => 
        string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    if (record == null)
        return Results.NotFound(new { error = "memory not found" });

    var updated = record with
    {
        Role = req.Role ?? record.Role,
        Content = req.Content ?? record.Content,
        Tags = req.Tags ?? record.Tags,
        Context = req.Context ?? record.Context
    };

    records[records.IndexOf(record)] = updated;
    await WriteAllRecordsAsync(records);

    return Results.Ok(new { status = "ok", id });
})
.WithName("UpdateMemory")
.WithDescription("Update an existing memory by ID (partial updates supported)")
.WithTags("memory-api")
.Accepts<UpdateMemoryRequest>("application/json")
.Produces<object>(200)
.Produces<object>(404);

app.MapDelete("/memory/{id}", async (string id) =>
{
    var records = await ReadAllRecordsAsync();
    var toRemove = records.FirstOrDefault(r => 
        string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    if (toRemove != null)
    {
        records.Remove(toRemove);
        await WriteAllRecordsAsync(records);
    }

    return Results.Ok(new { status = "ok" });
})
.WithName("DeleteMemory")
.WithDescription("Delete a specific memory by ID")
.WithTags("memory-api")
.Produces<object>(200);

app.MapDelete("/memory", async (
    [FromQuery] long? before,
    [FromQuery] string? tags,
    [FromQuery] string? role) =>
{
    if (!before.HasValue && string.IsNullOrWhiteSpace(tags) && string.IsNullOrWhiteSpace(role))
        return Results.BadRequest(new { error = "provide at least one filter: before, tags, or role" });

    var records = await ReadAllRecordsAsync();
    var tagSet = string.IsNullOrWhiteSpace(tags)
        ? new HashSet<string>()
        : tags.Split(',').Select(t => t.Trim().ToLowerInvariant()).ToHashSet();

    var filtered = records.Where(r =>
    {
        if (before.HasValue && r.Timestamp <= before.Value) return false;
        if (tagSet.Count > 0 && r.Tags.Any(t => tagSet.Contains(t.ToLowerInvariant()))) return false;
        if (!string.IsNullOrWhiteSpace(role) && r.Role == role.ToLowerInvariant()) return false;
        return true;
    }).ToList();

    await WriteAllRecordsAsync(filtered);

    return Results.Ok(new { status = "ok" });
})
.WithName("BulkDeleteMemories")
.WithDescription("Bulk delete memories by filters: before timestamp, tags, or role")
.WithTags("memory-api")
.Produces<object>(200)
.Produces<object>(400);

app.MapDelete("/memory/all", async () =>
{
    await fileSemaphore.WaitAsync();
    try
    {
        if (File.Exists(CLAUDE_FILE))
            File.Delete(CLAUDE_FILE);
    }
    finally
    {
        fileSemaphore.Release();
    }

    return Results.Ok(new { status = "ok" });
})
.WithName("DeleteAllMemories")
.WithDescription("Nuclear option: delete all memories")
.WithTags("memory-api")
.Produces<object>(200);

app.Urls.Add("http://0.0.0.0:5000");
app.Run();

// ============================================================================
// MODELS
// ============================================================================

record MemoryRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tags")] List<string> Tags,
    [property: JsonPropertyName("context")] string? Context
);

record CreateMemoryRequest(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tags")] List<string>? Tags,
    [property: JsonPropertyName("context")] string? Context
);

record UpdateMemoryRequest(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tags")] List<string>? Tags,
    [property: JsonPropertyName("context")] string? Context
);