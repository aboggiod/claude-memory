# Claude Memory API

Production-grade persistent memory storage for Claude conversations with advanced indexing, deduplication, lifecycle management, and compression capabilities.

## Features

### Core Functionality
- ✅ **CRUD Operations**: Full create, read, update, delete support for memories
- ✅ **JSONL Persistence**: Durable storage in newline-delimited JSON format
- ✅ **Advanced Filtering**: Query by timestamp, role, tags, context, and content
- ✅ **Swagger/OpenAPI**: Built-in API documentation at `/swagger`

### Advanced Features (New!)
- 🔥 **Write-Ahead Logging (WAL)**: Crash-safe operations with automatic compaction
- 🔍 **Full-Text Indexing**: In-memory inverted index for fast searches
- 🎯 **Smart Ranking**: Multi-factor scoring (similarity, recency, importance, frequency)
- 🔄 **Deduplication**: Automatic duplicate detection with configurable modes
- 📦 **Compression**: Summarize multiple memories into one (no external LLM)
- ⏱️ **Lifecycle Management**: TTL expiration, access tracking, archiving, pinning
- ⚙️ **Configurable**: Full control via appsettings.json or environment variables

## Quick Start

### Option 1: Docker (Recommended)

```bash
# Clone the repository
git clone <repo-url>
cd claude-memory

# Start with docker-compose
docker-compose up -d

# API is now available at http://localhost:5000
# Swagger UI at http://localhost:5000/swagger
```

### Option 2: Direct .NET Run

```bash
# Requires .NET 9.0 SDK
dotnet run

# Or build and run
dotnet build
dotnet run --project ClaudeMemoryApi.csproj
```

### Option 3: Systemd (Linux)

See `deployment/systemd/README.md` for detailed instructions.

## API Endpoints

### Core Endpoints (Backward Compatible)

- **GET /** - API information
- **GET /health** - Health check
- **POST /memory** - Create a new memory
- **GET /memory** - Query memories with filters
- **PUT /memory/{id}** - Update a memory
- **DELETE /memory/{id}** - Delete a specific memory
- **DELETE /memory** - Bulk delete with filters
- **DELETE /memory/all** - Delete all memories

### New Endpoints

- **POST /memory/compress** - Compress/summarize multiple memories

## Configuration

Configuration can be set via `appsettings.json` or environment variables.

### Environment Variable Examples

```bash
# Storage
Memory__DataDirectory=/data
Memory__StorageEngine=simple  # or "wal" for advanced storage

# WAL Settings (only if StorageEngine=wal)
Memory__WalMaxOperationsBeforeCompaction=1000
Memory__WalMaxBytesBeforeCompaction=5000000

# Scoring Weights
Memory__SimilarityWeight=1.0
Memory__ImportanceWeight=0.5
Memory__RecencyWeight=0.3
Memory__AccessFrequencyWeight=0.2
Memory__TagMatchWeight=0.3
Memory__RecencyHalfLifeDays=30.0

# Deduplication
Memory__EnableDeduplication=true
Memory__DeduplicationMode=mark  # "off", "mark", or "merge"
Memory__DeduplicationThreshold=0.85
```

### Storage Engines

#### Simple (Default)
- Single JSONL file storage
- Backward compatible with original implementation
- Best for most use cases

#### WAL (Advanced)
- Write-ahead logging for crash safety
- Automatic compaction
- In-memory index for faster queries
- Archive support for expired memories
- Best for high-volume or mission-critical scenarios

## Usage Examples

### Create a Memory

```bash
curl -X POST http://localhost:5000/memory \
  -H "Content-Type: application/json" \
  -d '{
    "role": "user",
    "content": "I love traveling to Japan",
    "tags": ["travel", "japan", "preference"],
    "context": "Personal preferences"
  }'
```

### Query Memories

```bash
# Get all memories
curl http://localhost:5000/memory

# Filter by tags
curl "http://localhost:5000/memory?tags=travel,japan"

# Search by content
curl "http://localhost:5000/memory?content=japan&limit=10"

# Include expired memories
curl "http://localhost:5000/memory?includeExpired=true"
```

### Update a Memory

```bash
curl -X PUT http://localhost:5000/memory/{id} \
  -H "Content-Type: application/json" \
  -d '{
    "tags": ["travel", "japan", "preference", "updated"]
  }'
```

### Compress Memories

```bash
curl -X POST http://localhost:5000/memory/compress \
  -H "Content-Type: application/json" \
  -d '{
    "ids": ["id1", "id2", "id3"],
    "kind": "summary",
    "importance": 4,
    "context": "Travel memories summary"
  }'
```

## Advanced Features

### Deduplication Modes

- **off**: Disable deduplication (always create new record)
- **mark**: Create new record but set `duplicateOfId` field
- **merge**: Update existing record instead (tags merged, access count incremented)

### Memory Scoring

Memories are ranked using a multi-factor score:

```
TotalScore = (Similarity × 1.0)
           + (Importance × 0.5)
           + (Recency × 0.3)
           + (AccessFrequency × 0.2)
           + (TagMatch × 0.3)
```

All weights are configurable.

### Lifecycle Management

- **TTL Expiration**: Set `expiresAtUnixMs` for automatic expiration
- **Access Tracking**: Auto-increment `accessCount` and update `lastAccessedUnixMs`
- **Pinning**: Set `importance` to 5 to prevent archiving
- **Archiving**: Old, low-importance memories automatically archived (if using WAL storage)

## Data Model

### External API (Backward Compatible)

```json
{
  "id": "string",
  "timestamp": 1234567890000,
  "role": "user",
  "content": "string",
  "tags": ["tag1", "tag2"],
  "context": "string"
}
```

### Internal Model (Enhanced)

The internal model includes additional fields:
- `userId`, `namespace`, `collection` - for multi-tenant support
- `kind` - memory type (fact, event, summary, preference, log)
- `importance` - priority (1-5)
- `expiresAtUnixMs` - TTL expiration
- `lastAccessedUnixMs` - last access time
- `accessCount` - access frequency
- `sourceIds` - references (for summaries)
- `duplicateOfId` - deduplication tracking

## File Structure

```
claude-memory/
├── Models/              # Data models
├── Storage/             # Storage implementations
│   ├── IMemoryStore.cs
│   ├── JsonLinesMemoryStore.cs (simple)
│   └── WalMemoryStore.cs (advanced)
├── Services/            # Business logic
│   ├── MemoryQueryService.cs
│   ├── MemoryLifecycleService.cs
│   ├── DeduplicationService.cs
│   ├── CompressionService.cs
│   ├── MemoryIndex.cs
│   └── TextTokenizer.cs
├── Configuration/       # Options classes
├── deployment/          # Deployment configs
│   └── systemd/
├── Program.cs           # Application entry point
├── appsettings.json     # Configuration
├── Dockerfile           # Container build
└── docker-compose.yaml  # Container orchestration
```

## Development

### Prerequisites

- .NET 9.0 SDK
- (Optional) Docker for containerized deployment

### Build

```bash
dotnet build
```

### Run Tests

```bash
dotnet test
```

### Run Locally

```bash
dotnet run
```

## Deployment

### Docker

```bash
docker build -t claude-memory-api .
docker run -d -p 5000:5000 -v $(pwd)/data:/data claude-memory-api
```

### Docker Compose

```bash
docker-compose up -d
docker-compose logs -f  # View logs
docker-compose down     # Stop
```

### Systemd (Linux)

See `deployment/systemd/README.md` for complete instructions.

## Architecture

### Backward Compatibility

All original endpoints and behavior are preserved:
- Same API paths and HTTP verbs
- Same request/response JSON schemas
- Same query parameters
- JSONL storage format enhanced but backward-compatible

### Design Principles

1. **No Breaking Changes**: External contracts remain unchanged
2. **No External Dependencies**: Pure .NET, no vector DBs or LLMs
3. **Deterministic**: All operations are reproducible
4. **Configurable**: Defaults preserve original behavior
5. **Production-Ready**: WAL, crash safety, proper error handling

## Troubleshooting

### Connection Refused

Ensure the API is running:
```bash
curl http://localhost:5000/health
```

### Data Not Persisting

Check data directory permissions:
```bash
ls -la /data  # Docker
ls -la C:\Temp\ai-memory  # Windows local
```

### Performance Issues

Consider switching to WAL storage:
```bash
Memory__StorageEngine=wal
```

## License

[Specify your license here]

## Contributing

[Specify contribution guidelines here]

## Support

For issues and questions:
- GitHub Issues: [your-repo-url/issues]
- Documentation: See `/swagger` endpoint
- Deployment Guide: `deployment/systemd/README.md`
