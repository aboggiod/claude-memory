# OpenAI MCP Connector - Production Ready

A fully compliant OpenAI GPT Actions connector implementing the Model Context Protocol (MCP) 2024-11-05 specification with OAuth 2.0 + PKCE authentication.

## 🎯 Features

### ✅ OAuth 2.0 Compliance
- **RFC 6749**: Authorization Code Grant Flow
- **RFC 7636**: PKCE (Proof Key for Code Exchange) with S256
- **RFC 7517**: JSON Web Key Set (JWKS)
- **RFC 7519**: JSON Web Tokens (JWT)
- **RFC 7591**: Dynamic Client Registration
- **RFC 8414**: OAuth 2.0 Authorization Server Metadata
- **RFC 8707**: OAuth 2.0 Protected Resource Metadata

### ✅ MCP Protocol 2024-11-05
- JSON-RPC 2.0 compliant messages
- Proper lifecycle management (initialize, notifications)
- Tool definitions with input schemas
- Content-based responses

### ✅ Security Features
- RSA-256 signed JWT tokens
- Automatic token validation
- PKCE flow protection
- Secure authorization flow
- Environment-based configuration
- CORS support for cross-origin requests

### ✅ .NET 8 Best Practices
- Minimal APIs
- Built-in authentication middleware
- Proper dependency injection
- Structured logging
- Configuration from environment variables

## 📋 Requirements

- .NET 8.0 SDK or later
- SQLite (included via NuGet)
- ngrok or similar tunneling tool (for OpenAI integration)

## 🚀 Quick Start

### 1. Clone and Setup

```bash
cd /path/to/openai-mcp-connector
cp .env.example .env
```

### 2. Configure Environment

Edit `.env`:

```env
OAUTH_ISSUER=https://your-unique-id.ngrok.io
OAUTH_AUDIENCE=gpt-memory-api
SERVICE_NAME=gpt-memory
DB_PATH=memory.db
RSA_KEY_PATH=rsa-private.pem
```

### 3. Build and Run

```bash
dotnet build OpenAIMcpConnector.csproj
dotnet run --project OpenAIMcpConnector.csproj
```

The server will start on `http://0.0.0.0:5001`

### 4. Expose with ngrok

```bash
ngrok http 5001
```

Copy the HTTPS URL (e.g., `https://abc123.ngrok.io`) and update your `.env` file with it.

## 🔧 Configuration for OpenAI GPT Actions

### Step 1: Create Custom GPT

1. Go to https://chat.openai.com
2. Click "Explore GPTs" → "Create"
3. Configure your GPT with name and instructions

### Step 2: Add Action

In the Actions section, click "Create new action" and use this schema:

```yaml
openapi: 3.1.0
info:
  title: GPT Memory API
  description: Persistent memory storage with threading and friction logging
  version: 3.0.0
servers:
  - url: https://your-unique-id.ngrok.io
paths:
  /:
    post:
      operationId: executeMcpTool
      summary: Execute MCP tool
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              properties:
                jsonrpc:
                  type: string
                  example: "2.0"
                method:
                  type: string
                  example: "tools/call"
                params:
                  type: object
                  properties:
                    name:
                      type: string
                    arguments:
                      type: object
                id:
                  type: number
      responses:
        '200':
          description: Successful response
          content:
            application/json:
              schema:
                type: object
```

### Step 3: Configure OAuth

1. **Authentication Type**: OAuth
2. **Client ID**: (will be auto-generated via dynamic registration)
3. **Authorization URL**: `https://your-unique-id.ngrok.io/authorize`
4. **Token URL**: `https://your-unique-id.ngrok.io/token`
5. **Scope**: `mcp.read mcp.write`
6. **Token Exchange Method**: Default (Authorization Code + PKCE)

## 🛠️ Available MCP Tools

### Key-Value Storage
- `store_memory` - Store key-value pairs
- `retrieve_memory` - Retrieve by key
- `list_keys` - List all keys (with optional prefix filter)
- `delete_memory` - Delete by key
- `search_memory` - Search in keys/values

### Thread Management
- `store_thread_entry` - Add entry to conversation thread
- `get_thread` - Retrieve thread entries
- `list_threads` - List all threads
- `update_thread_metadata` - Update thread status/category

### Entry Management
- `journal_entry` - Create daily journal entry
- `recent_entries` - Get recent activity
- `update_entry` - Update entry content
- `add_tags` - Tag entries
- `search_entries` - Search with filters

### Friction Logger
- `friction_log` - Log development friction
- `friction_review` - Review logged friction
- `friction_resolve` - Mark friction as resolved

## 📊 API Endpoints

### OAuth Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/.well-known/oauth-authorization-server` | GET | OAuth server metadata |
| `/.well-known/oauth-protected-resource` | GET | Protected resource metadata |
| `/.well-known/jwks.json` | GET | JSON Web Key Set |
| `/registration` | POST | Dynamic client registration |
| `/authorize` | GET/POST | Authorization endpoint |
| `/token` | POST | Token exchange endpoint |

### MCP Endpoints

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/` | GET | No | Server info |
| `/` | POST | Yes (JWT) | JSON-RPC 2.0 endpoint |
| `/` | DELETE | Yes (JWT) | Session termination |

## 🔐 Security Considerations

### Production Deployment Checklist

- [ ] Use HTTPS in production (required for OAuth)
- [ ] Store RSA keys securely (use key management service)
- [ ] Rotate RSA keys regularly
- [ ] Implement rate limiting
- [ ] Add request size limits
- [ ] Enable structured logging
- [ ] Monitor for suspicious activity
- [ ] Use secure session storage
- [ ] Implement backup strategy for SQLite database
- [ ] Add health check endpoints
- [ ] Configure proper CORS policies
- [ ] Use environment-specific configurations

## 🧪 Testing

### Test OAuth Flow

```bash
# 1. Start server
dotnet run

# 2. Test metadata endpoint
curl https://your-ngrok-url/.well-known/oauth-authorization-server

# 3. Test JWKS endpoint
curl https://your-ngrok-url/.well-known/jwks.json
```

### Test MCP Tools (requires valid JWT)

```bash
# Get access token from OAuth flow first, then:
curl -X POST https://your-ngrok-url/ \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/list",
    "id": 1
  }'
```

## 📚 Specification References

This implementation follows these official specifications:

### OAuth 2.0
- [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749) - OAuth 2.0 Authorization Framework
- [RFC 7636](https://datatracker.ietf.org/doc/html/rfc7636) - PKCE
- [RFC 8414](https://datatracker.ietf.org/doc/html/rfc8414) - Authorization Server Metadata

### JWT & JOSE
- [RFC 7519](https://datatracker.ietf.org/doc/html/rfc7519) - JSON Web Tokens
- [RFC 7517](https://datatracker.ietf.org/doc/html/rfc7517) - JSON Web Key (JWK)

### MCP
- [MCP Specification 2024-11-05](https://spec.modelcontextprotocol.io/specification/2024-11-05/)
- [JSON-RPC 2.0](https://www.jsonrpc.org/specification)

### OpenAI
- [OpenAI GPT Actions Documentation](https://platform.openai.com/docs/actions)
- [OpenAI Authentication](https://platform.openai.com/docs/actions/authentication/oauth)

## 🐛 Troubleshooting

### "Invalid or expired authorization code"
- Authorization codes expire after 5 minutes
- Codes are single-use only
- Check system time synchronization

### "PKCE verification failed"
- Ensure code_verifier is UTF-8 encoded before hashing
- Verify SHA-256 is being used
- Check base64url encoding (no padding)

### "Token validation failed"
- Verify ISSUER matches exactly
- Check token hasn't expired
- Ensure JWKS endpoint is accessible

### Database Errors
- Ensure `schema.sql` exists in the same directory
- Check write permissions for SQLite database
- Verify SQLite file isn't locked by another process

## 📝 License

MIT License - See LICENSE file for details

## 🤝 Contributing

Contributions welcome! Please ensure:
- Follow existing code style
- Add tests for new features
- Update documentation
- Follow semantic versioning

## 📞 Support

For issues and questions:
- GitHub Issues: [Your Repository URL]
- Documentation: This README
- Specifications: See references above

---

**Built with .NET 8, following 2025 best practices and latest specifications** ✨
