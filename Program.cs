using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

// =========================
// CONFIGURATION FROM ENVIRONMENT
// =========================
var ISSUER = Environment.GetEnvironmentVariable("OAUTH_ISSUER") ?? "https://localhost:5001";
var AUDIENCE = Environment.GetEnvironmentVariable("OAUTH_AUDIENCE") ?? "gpt-memory-api";
var SERVICE_NAME = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "gpt-memory";
var DB_PATH = Environment.GetEnvironmentVariable("DB_PATH") ?? "memory.db";
var KEY_PATH = Environment.GetEnvironmentVariable("RSA_KEY_PATH") ?? "rsa-private.pem";
var CONNECTION_STRING = $"Data Source={DB_PATH}";

// =========================
// DATABASE SETUP
// =========================
if (!File.Exists("schema.sql"))
{
    Console.WriteLine("ERROR: schema.sql not found!");
    Environment.Exit(1);
}

Console.WriteLine("Initializing SQLite database...");
using var initConnection = new SqliteConnection(CONNECTION_STRING);
initConnection.Open();
using var initCommand = initConnection.CreateCommand();
initCommand.CommandText = File.ReadAllText("schema.sql");
initCommand.ExecuteNonQuery();
Console.WriteLine("SQLite database initialized successfully.");

// =========================
// RSA KEY LOADING
// =========================
RSA rsa;
if (File.Exists(KEY_PATH))
{
    Console.WriteLine($"Loading RSA key from {KEY_PATH}...");
    rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(KEY_PATH));
}
else
{
    Console.WriteLine("Generating new RSA key...");
    rsa = RSA.Create(2048);
    File.WriteAllText(KEY_PATH, rsa.ExportRsaPrivateKeyPem());
    Console.WriteLine($"RSA key saved to {KEY_PATH}");
}

var signingKey = new RsaSecurityKey(rsa.ExportParameters(true))
{
    KeyId = Guid.NewGuid().ToString("N")
};
var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

// =========================
// BUILD APP
// =========================
var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on all interfaces
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001);
});

// Add authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = ISSUER,
            ValidateAudience = true,
            ValidAudience = AUDIENCE,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Custom event handlers for better error logging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Console.WriteLine("Token validated successfully");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Mcp-Session-Id");
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// =========================
// AUTH CODE STORE (PKCE)
// =========================
var authCodes = new ConcurrentDictionary<string, AuthCode>();

// Cleanup expired auth codes periodically
var cleanupTimer = new Timer(_ =>
{
    var now = DateTime.UtcNow;
    var expiredKeys = authCodes.Where(kvp => kvp.Value.ExpiresAt < now).Select(kvp => kvp.Key).ToList();
    foreach (var key in expiredKeys)
    {
        authCodes.TryRemove(key, out _);
    }
}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

// =========================
// HELPER: Base64Url Encoding (RFC 7636 §3 / RFC 4648 §5)
// =========================
static string Base64UrlEncode(byte[] input)
{
    return Convert.ToBase64String(input)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

// =========================
// JWKS ENDPOINT (RFC 7517)
// =========================
app.MapGet("/.well-known/jwks.json", () =>
{
    var p = rsa.ExportParameters(false);
    return Results.Json(new
    {
        keys = new[]
        {
            new
            {
                kty = "RSA",
                use = "sig",
                kid = signingKey.KeyId,
                alg = "RS256",
                n = Base64UrlEncode(p.Modulus!),
                e = Base64UrlEncode(p.Exponent!)
            }
        }
    });
});

// =========================
// OAUTH AUTHORIZATION SERVER METADATA (RFC 8414)
// =========================
app.MapGet("/.well-known/oauth-authorization-server", () =>
{
    return Results.Json(new
    {
        issuer = ISSUER,
        authorization_endpoint = $"{ISSUER}/authorize",
        token_endpoint = $"{ISSUER}/token",
        jwks_uri = $"{ISSUER}/.well-known/jwks.json",
        registration_endpoint = $"{ISSUER}/registration",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code" },
        code_challenge_methods_supported = new[] { "S256" },
        token_endpoint_auth_methods_supported = new[] { "none" },
        scopes_supported = new[] { "mcp.read", "mcp.write" }
    });
});

// =========================
// DYNAMIC CLIENT REGISTRATION (RFC 7591)
// =========================
app.MapPost("/registration", async (HttpRequest request) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();

        // In production, implement proper client registration with unique IDs
        var clientId = "gpt-memory-client-" + Guid.NewGuid().ToString("N")[..8];

        return Results.Json(new
        {
            client_id = clientId,
            client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            client_secret_expires_at = 0,
            grant_types = new[] { "authorization_code" },
            response_types = new[] { "code" },
            scope = "mcp.read mcp.write",
            token_endpoint_auth_method = "none",
            application_type = "web"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Registration error: {ex.Message}");
        return Results.BadRequest(new { error = "invalid_client_metadata" });
    }
});

// =========================
// OAUTH PROTECTED RESOURCE METADATA (RFC 8707)
// =========================
app.MapGet("/.well-known/oauth-protected-resource", () =>
{
    return Results.Json(new
    {
        resource = ISSUER,
        authorization_servers = new[] { ISSUER },
        bearer_methods_supported = new[] { "header" },
        resource_signing_alg_values_supported = new[] { "RS256" }
    });
});

// =========================
// AUTHORIZE ENDPOINT (RFC 6749 + RFC 7636)
// =========================
app.MapMethods("/authorize", new[] { "GET", "POST" }, async (HttpContext ctx) =>
{
    // POST: User approved authorization
    if (ctx.Request.Method == "POST")
    {
        var form = await ctx.Request.ReadFormAsync();
        var code = form["code"].ToString();

        if (string.IsNullOrEmpty(code) || !authCodes.TryGetValue(code, out var auth))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("<!DOCTYPE html><html><body><h1>Error</h1><p>Invalid or expired authorization code</p></body></html>");
            return;
        }

        // Don't remove code yet - it's needed for token exchange
        var redirect = $"{auth.RedirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(auth.State)}";
        ctx.Response.Redirect(redirect);
        return;
    }

    // GET: Display authorization prompt
    var q = ctx.Request.Query;

    // Validate required parameters (RFC 6749 §4.1.1)
    if (q["response_type"] != "code")
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("<!DOCTYPE html><html><body><h1>Error</h1><p>unsupported_response_type</p></body></html>");
        return;
    }

    // Validate PKCE parameters (RFC 7636 §4.3)
    if (q["code_challenge_method"] != "S256")
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("<!DOCTYPE html><html><body><h1>Error</h1><p>Invalid code_challenge_method. Only S256 is supported.</p></body></html>");
        return;
    }

    if (string.IsNullOrEmpty(q["code_challenge"]) ||
        string.IsNullOrEmpty(q["state"]) ||
        string.IsNullOrEmpty(q["redirect_uri"]))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("<!DOCTYPE html><html><body><h1>Error</h1><p>invalid_request: Missing required parameters</p></body></html>");
        return;
    }

    // Generate authorization code
    var authCode = Guid.NewGuid().ToString("N");
    authCodes[authCode] = new AuthCode(
        Code: authCode,
        CodeChallenge: q["code_challenge"]!,
        State: q["state"]!,
        RedirectUri: q["redirect_uri"]!,
        ExpiresAt: DateTime.UtcNow.AddMinutes(5)
    );

    // Display authorization prompt
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync($$"""
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Authorize {{{SERVICE_NAME}}}</title>
            <style>
                body {
                    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
                    background: #0a0a0a;
                    color: #ffffff;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    min-height: 100vh;
                    margin: 0;
                    padding: 20px;
                }
                .container {
                    text-align: center;
                    max-width: 500px;
                    background: #1a1a1a;
                    border-radius: 16px;
                    padding: 40px;
                    box-shadow: 0 8px 32px rgba(0,0,0,0.4);
                }
                h1 {
                    font-size: 28px;
                    margin: 0 0 10px;
                    color: #ffffff;
                }
                .subtitle {
                    color: #888;
                    margin-bottom: 30px;
                    font-size: 14px;
                }
                .permissions {
                    background: #252525;
                    border-radius: 8px;
                    padding: 20px;
                    margin: 20px 0;
                    text-align: left;
                }
                .permissions h3 {
                    margin: 0 0 15px;
                    font-size: 16px;
                    color: #fff;
                }
                .permissions ul {
                    margin: 0;
                    padding: 0;
                    list-style: none;
                }
                .permissions li {
                    padding: 8px 0;
                    color: #ccc;
                    font-size: 14px;
                }
                .permissions li::before {
                    content: "✓ ";
                    color: #4CAF50;
                    font-weight: bold;
                    margin-right: 8px;
                }
                button {
                    padding: 16px 48px;
                    font-size: 16px;
                    font-weight: 600;
                    background: #10a37f;
                    color: white;
                    border: none;
                    border-radius: 8px;
                    cursor: pointer;
                    transition: background 0.2s;
                    width: 100%;
                }
                button:hover {
                    background: #0d8c6d;
                }
                button:active {
                    transform: scale(0.98);
                }
                .footer {
                    margin-top: 20px;
                    font-size: 12px;
                    color: #666;
                }
            </style>
        </head>
        <body>
            <div class="container">
                <h1>{{{SERVICE_NAME}}}</h1>
                <p class="subtitle">v3.0</p>

                <div class="permissions">
                    <h3>This application will be able to:</h3>
                    <ul>
                        <li>Read your stored memories</li>
                        <li>Write and update memories</li>
                        <li>Manage conversation threads</li>
                        <li>Track development friction points</li>
                    </ul>
                </div>

                <form method="post">
                    <input type="hidden" name="code" value="{{{authCode}}}">
                    <button type="submit">Allow Access</button>
                </form>

                <p class="footer">By authorizing, you agree to share data with ChatGPT</p>
            </div>
        </body>
        </html>
        """);
});

// =========================
// TOKEN ENDPOINT (RFC 6749 §4.1.3 + RFC 7636)
// =========================
app.MapPost("/token", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();

        var grantType = form["grant_type"].ToString();
        var code = form["code"].ToString();
        var codeVerifier = form["code_verifier"].ToString();
        var redirectUri = form["redirect_uri"].ToString();

        // Validate grant_type (RFC 6749 §4.1.3)
        if (grantType != "authorization_code")
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "unsupported_grant_type" });
            return;
        }

        // Validate required parameters
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(redirectUri))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "invalid_request", error_description = "Missing required parameters" });
            return;
        }

        // Verify authorization code
        if (!authCodes.TryRemove(code, out var auth))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "invalid_grant", error_description = "Invalid or expired authorization code" });
            return;
        }

        // Check expiration
        if (auth.ExpiresAt < DateTime.UtcNow)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "invalid_grant", error_description = "Authorization code expired" });
            return;
        }

        // Verify redirect_uri matches (RFC 6749 §4.1.3)
        if (redirectUri != auth.RedirectUri)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "invalid_grant", error_description = "redirect_uri mismatch" });
            return;
        }

        // PKCE verification (RFC 7636 §4.6)
        // Hash the code_verifier with SHA-256 and base64url encode
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var computedChallenge = Base64UrlEncode(hashBytes);

        if (computedChallenge != auth.CodeChallenge)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "invalid_grant", error_description = "PKCE verification failed" });
            return;
        }

        // Issue JWT access token (RFC 7519)
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, SERVICE_NAME),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("scope", "mcp.read mcp.write")
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = ISSUER,
            Audience = AUDIENCE,
            SigningCredentials = signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(token);

        Console.WriteLine("Access token issued successfully");

        // RFC 6749 §5.1: Successful response
        await ctx.Response.WriteAsJsonAsync(new
        {
            access_token = jwtString,
            token_type = "Bearer",
            expires_in = 3600,
            scope = "mcp.read mcp.write"
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Token endpoint error: {ex.Message}");
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = "server_error" });
    }
});

// =========================
// MCP SESSION STORE
// =========================
var mcpSessions = new ConcurrentDictionary<string, DateTime>();

// Cleanup expired MCP sessions periodically
var mcpCleanupTimer = new Timer(_ =>
{
    var expiry = DateTime.UtcNow.AddHours(-24);
    var expiredSessions = mcpSessions.Where(kvp => kvp.Value < expiry).Select(kvp => kvp.Key).ToList();
    foreach (var session in expiredSessions)
    {
        mcpSessions.TryRemove(session, out _);
    }
}, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

// =========================
// ROOT / MCP ENDPOINT (JSON-RPC 2.0)
// =========================
app.MapGet("/", async ctx =>
{
    // Public endpoint - no auth required for GET
    await ctx.Response.WriteAsJsonAsync(new
    {
        name = SERVICE_NAME,
        version = "3.0.0",
        protocol = "MCP/2024-11-05",
        authorization = new
        {
            type = "OAuth 2.0",
            authorization_endpoint = $"{ISSUER}/authorize",
            token_endpoint = $"{ISSUER}/token"
        }
    });
});

app.MapPost("/").RequireAuthorization(async (HttpContext ctx) =>
{
    try
    {
        // Session management
        var sessionId = ctx.Request.Headers["Mcp-Session-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = Guid.NewGuid().ToString("N");
        }
        mcpSessions[sessionId] = DateTime.UtcNow;
        ctx.Response.Headers.Append("Mcp-Session-Id", sessionId);

        // Parse JSON-RPC request
        JsonNode? json;
        try
        {
            json = await JsonNode.ParseAsync(ctx.Request.Body);
        }
        catch
        {
            await ctx.Response.WriteAsJsonAsync(new
            {
                jsonrpc = "2.0",
                error = new { code = -32700, message = "Parse error" },
                id = (string?)null
            });
            return;
        }

        var method = json?["method"]?.GetValue<string>() ?? "";
        var id = json?["id"];

        // Handle notifications (no response required)
        if (method == "notifications/initialized")
        {
            ctx.Response.StatusCode = 202;
            return;
        }

        // Build JSON-RPC response
        var resp = new JsonObject { ["jsonrpc"] = "2.0" };
        if (id != null)
        {
            resp["id"] = id.DeepClone();
        }

        try
        {
            switch (method)
            {
                case "initialize":
                    resp["result"] = new JsonObject
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new JsonObject
                        {
                            ["tools"] = new JsonObject()
                        },
                        ["serverInfo"] = new JsonObject
                        {
                            ["name"] = SERVICE_NAME,
                            ["version"] = "3.0.0"
                        }
                    };
                    break;

                case "tools/list":
                    var toolsJson = await File.ReadAllTextAsync("tools.json");
                    resp["result"] = JsonNode.Parse(toolsJson);
                    break;

                case "tools/call":
                    var tool = json!["params"]!["name"]!.GetValue<string>();
                    var args = json["params"]!["arguments"]!.AsObject();

                    resp["result"] = tool switch
                    {
                        "store_memory" => StoreMemory(args),
                        "retrieve_memory" => RetrieveMemory(args),
                        "list_keys" => ListKeys(args),
                        "delete_memory" => DeleteMemory(args),
                        "search_memory" => SearchMemory(args),
                        "store_thread_entry" => StoreThreadEntry(args),
                        "get_thread" => GetThread(args),
                        "list_threads" => ListThreads(args),
                        "add_tags" => AddTags(args),
                        "search_entries" => SearchEntries(args),
                        "journal_entry" => JournalEntry(args),
                        "recent_entries" => RecentEntries(args),
                        "update_entry" => UpdateEntry(args),
                        "update_thread_metadata" => UpdateThreadMetadata(args),
                        "friction_log" => FrictionLog(args),
                        "friction_review" => FrictionReview(args),
                        "friction_resolve" => FrictionResolve(args),
                        _ => throw new Exception("Unknown tool: " + tool)
                    };
                    break;

                default:
                    resp["error"] = new JsonObject
                    {
                        ["code"] = -32601,
                        ["message"] = "Method not found"
                    };
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Tool execution error: {e.Message}");
            resp["error"] = new JsonObject
            {
                ["code"] = -32603,
                ["message"] = e.Message
            };
        }

        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(resp.ToJsonString());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"MCP endpoint error: {ex.Message}");
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new
        {
            jsonrpc = "2.0",
            error = new { code = -32603, message = "Internal error" },
            id = (string?)null
        });
    }
});

app.MapDelete("/").RequireAuthorization(async ctx =>
{
    var sessionId = ctx.Request.Headers["Mcp-Session-Id"].FirstOrDefault();
    if (!string.IsNullOrEmpty(sessionId))
    {
        mcpSessions.TryRemove(sessionId, out _);
        Console.WriteLine($"Session {sessionId} terminated");
    }
    ctx.Response.StatusCode = 200;
});

// =========================
// TOOL IMPLEMENTATIONS - KEY-VALUE STORE
// =========================
JsonObject StoreMemory(JsonObject args)
{
    var key = args["key"]!.GetValue<string>();
    var value = args["value"]!.GetValue<string>();

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT INTO kv_store (key, value, updated_at)
        VALUES ($key, $value, CURRENT_TIMESTAMP)
        ON CONFLICT(key) DO UPDATE SET
            value = $value,
            updated_at = CURRENT_TIMESTAMP";
    cmd.Parameters.AddWithValue("$key", key);
    cmd.Parameters.AddWithValue("$value", value);
    cmd.ExecuteNonQuery();

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"Memory stored successfully: {key}"
            }
        }
    };
}

JsonObject RetrieveMemory(JsonObject args)
{
    var key = args["key"]!.GetValue<string>();

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT value FROM kv_store WHERE key = $key";
    cmd.Parameters.AddWithValue("$key", key);
    var value = cmd.ExecuteScalar() as string;

    if (value == null)
    {
        throw new Exception($"Memory not found: {key}");
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = value
            }
        }
    };
}

JsonObject ListKeys(JsonObject args)
{
    var prefix = args.TryGetPropertyValue("prefix", out var p) ? p!.GetValue<string>() : "";

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();

    if (string.IsNullOrEmpty(prefix))
    {
        cmd.CommandText = "SELECT key FROM kv_store ORDER BY key";
    }
    else
    {
        cmd.CommandText = "SELECT key FROM kv_store WHERE key LIKE $prefix ORDER BY key";
        cmd.Parameters.AddWithValue("$prefix", prefix + "%");
    }

    var keys = new List<string>();
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        keys.Add(reader.GetString(0));
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = JsonSerializer.Serialize(keys)
            }
        }
    };
}

JsonObject DeleteMemory(JsonObject args)
{
    var key = args["key"]!.GetValue<string>();

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM kv_store WHERE key = $key";
    cmd.Parameters.AddWithValue("$key", key);

    var affected = cmd.ExecuteNonQuery();
    if (affected == 0)
    {
        throw new Exception($"Memory not found: {key}");
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"Memory deleted: {key}"
            }
        }
    };
}

JsonObject SearchMemory(JsonObject args)
{
    var query = args["query"]!.GetValue<string>();
    var searchIn = args.TryGetPropertyValue("search_in", out var si) ? si!.GetValue<string>() : "both";

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = searchIn switch
    {
        "keys" => "SELECT key FROM kv_store WHERE key LIKE $query",
        "values" => "SELECT key FROM kv_store WHERE value LIKE $query",
        _ => "SELECT key FROM kv_store WHERE key LIKE $query OR value LIKE $query"
    };
    cmd.Parameters.AddWithValue("$query", $"%{query}%");

    var results = new List<string>();
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        results.Add(reader.GetString(0));
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = JsonSerializer.Serialize(results)
            }
        }
    };
}

// =========================
// TOOL IMPLEMENTATIONS - THREADING
// =========================
JsonObject StoreThreadEntry(JsonObject args)
{
    var threadId = args["thread_id"]!.GetValue<string>();
    var content = args["content"]!.GetValue<string>();
    var entryType = args.TryGetPropertyValue("entry_type", out var et) ? et!.GetValue<string>() : "thought";
    var title = args.TryGetPropertyValue("title", out var t) && t != null ? t.GetValue<string>() : null;
    var tagsArray = args.TryGetPropertyValue("tags", out var tgs) && tgs != null ? tgs.AsArray() : null;

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var transaction = conn.BeginTransaction();

    try
    {
        // Get the last entry in this thread to link as parent
        long? parentId = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM entries WHERE thread_id = $threadId ORDER BY id DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$threadId", threadId);
            var result = cmd.ExecuteScalar();
            if (result != null)
            {
                parentId = (long)result;
            }
        }

        // Insert new entry
        long entryId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO entries (thread_id, parent_id, content, entry_type)
                VALUES ($threadId, $parentId, $content, $entryType)
                RETURNING id";
            cmd.Parameters.AddWithValue("$threadId", threadId);
            cmd.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$entryType", entryType);
            entryId = (long)cmd.ExecuteScalar()!;
        }

        // Add tags if provided
        if (tagsArray != null && tagsArray.Count > 0)
        {
            foreach (var tagNode in tagsArray)
            {
                var tag = tagNode!.GetValue<string>();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO tags (entry_id, tag) VALUES ($entryId, $tag)";
                cmd.Parameters.AddWithValue("$entryId", entryId);
                cmd.Parameters.AddWithValue("$tag", tag);
                cmd.ExecuteNonQuery();
            }
        }

        // Create or update thread metadata
        using (var cmd = conn.CreateCommand())
        {
            if (title != null && parentId == null)
            {
                cmd.CommandText = @"
                    INSERT INTO threads (id, title, updated_at)
                    VALUES ($threadId, $title, CURRENT_TIMESTAMP)
                    ON CONFLICT(id) DO UPDATE SET updated_at = CURRENT_TIMESTAMP";
                cmd.Parameters.AddWithValue("$title", title);
            }
            else
            {
                cmd.CommandText = @"
                    INSERT INTO threads (id, updated_at)
                    VALUES ($threadId, CURRENT_TIMESTAMP)
                    ON CONFLICT(id) DO UPDATE SET updated_at = CURRENT_TIMESTAMP";
            }
            cmd.Parameters.AddWithValue("$threadId", threadId);
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = JsonSerializer.Serialize(new { entry_id = entryId, thread_id = threadId })
                }
            }
        };
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}

JsonObject GetThread(JsonObject args)
{
    var threadId = args["thread_id"]!.GetValue<string>();
    var limit = args.TryGetPropertyValue("limit", out var l) && l != null ? l.GetValue<int>() : (int?)null;

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = limit.HasValue
        ? "SELECT id, content, created_at, entry_type FROM entries WHERE thread_id = $threadId ORDER BY id DESC LIMIT $limit"
        : "SELECT id, content, created_at, entry_type FROM entries WHERE thread_id = $threadId ORDER BY id";
    cmd.Parameters.AddWithValue("$threadId", threadId);
    if (limit.HasValue)
    {
        cmd.Parameters.AddWithValue("$limit", limit.Value);
    }

    var formatted = new StringBuilder();
    formatted.AppendLine($"Thread: {threadId}");
    formatted.AppendLine(new string('=', 60));
    formatted.AppendLine();

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var id = reader.GetInt64(0);
        var content = reader.GetString(1);
        var created = reader.GetString(2);
        var type = reader.GetString(3);

        formatted.AppendLine($"[Entry #{id}] {type} | {created}");
        formatted.AppendLine(content);
        formatted.AppendLine();
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = formatted.ToString()
            }
        }
    };
}

JsonObject ListThreads(JsonObject args)
{
    var status = args.TryGetPropertyValue("status", out var s) && s != null ? s.GetValue<string>() : null;
    var category = args.TryGetPropertyValue("category", out var c) && c != null ? c.GetValue<string>() : null;

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();

    var whereClauses = new List<string>();
    if (status != null) whereClauses.Add("status = $status");
    if (category != null) whereClauses.Add("category = $category");

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, title, status, category, updated_at FROM threads" +
        (whereClauses.Count > 0 ? " WHERE " + string.Join(" AND ", whereClauses) : "") +
        " ORDER BY updated_at DESC";

    if (status != null) cmd.Parameters.AddWithValue("$status", status);
    if (category != null) cmd.Parameters.AddWithValue("$category", category);

    var threads = new List<object>();
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        threads.Add(new
        {
            id = reader.GetString(0),
            title = reader.IsDBNull(1) ? null : reader.GetString(1),
            status = reader.GetString(2),
            category = reader.GetString(3),
            updated_at = reader.GetString(4)
        });
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = JsonSerializer.Serialize(threads)
            }
        }
    };
}

JsonObject AddTags(JsonObject args)
{
    var entryId = args["entry_id"]!.GetValue<long>();
    var tagsArray = args["tags"]!.AsArray();

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var transaction = conn.BeginTransaction();

    try
    {
        foreach (var tagNode in tagsArray)
        {
            var tag = tagNode!.GetValue<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO tags (entry_id, tag) VALUES ($entryId, $tag)";
            cmd.Parameters.AddWithValue("$entryId", entryId);
            cmd.Parameters.AddWithValue("$tag", tag);
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"Added {tagsArray.Count} tags to entry {entryId}"
                }
            }
        };
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}

JsonObject SearchEntries(JsonObject args)
{
    var query = args["query"]!.GetValue<string>();
    var dateFrom = args.TryGetPropertyValue("date_from", out var df) && df != null ? df.GetValue<string>() : null;
    var dateTo = args.TryGetPropertyValue("date_to", out var dt) && dt != null ? dt.GetValue<string>() : null;
    var tagsArray = args.TryGetPropertyValue("tags", out var t) && t != null ? t.AsArray() : null;

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();

    var whereClauses = new List<string> { "content LIKE $query" };
    if (dateFrom != null) whereClauses.Add("created_at >= $dateFrom");
    if (dateTo != null) whereClauses.Add("created_at <= $dateTo");

    var sql = "SELECT DISTINCT e.id, e.thread_id, e.content, e.created_at, e.entry_type FROM entries e";

    if (tagsArray != null && tagsArray.Count > 0)
    {
        sql += " JOIN tags t ON e.id = t.entry_id";
        whereClauses.Add($"t.tag IN ({string.Join(",", tagsArray.Select((_, i) => $"$tag{i}"))})");
    }

    sql += " WHERE " + string.Join(" AND ", whereClauses) + " ORDER BY e.created_at DESC LIMIT 50";

    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("$query", $"%{query}%");
    if (dateFrom != null) cmd.Parameters.AddWithValue("$dateFrom", dateFrom);
    if (dateTo != null) cmd.Parameters.AddWithValue("$dateTo", dateTo);
    if (tagsArray != null)
    {
        for (int i = 0; i < tagsArray.Count; i++)
        {
            cmd.Parameters.AddWithValue($"$tag{i}", tagsArray[i]!.GetValue<string>());
        }
    }

    var results = new List<object>();
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        results.Add(new
        {
            id = reader.GetInt64(0),
            thread_id = reader.GetString(1),
            content = reader.GetString(2),
            created_at = reader.GetString(3),
            entry_type = reader.GetString(4)
        });
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = JsonSerializer.Serialize(results)
            }
        }
    };
}

JsonObject JournalEntry(JsonObject args)
{
    var content = args["content"]!.GetValue<string>();
    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var threadId = $"journal_{today}";

    var threadArgs = new JsonObject
    {
        ["thread_id"] = threadId,
        ["content"] = content,
        ["entry_type"] = "journal"
    };

    return StoreThreadEntry(threadArgs);
}

JsonObject RecentEntries(JsonObject args)
{
    var limit = args.TryGetPropertyValue("limit", out var l) && l != null ? l.GetValue<int>() : 20;
    var threadFilter = args.TryGetPropertyValue("thread_id", out var tf) && tf != null ? tf.GetValue<string>() : null;

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();

    if (threadFilter != null)
    {
        cmd.CommandText = @"
            SELECT e.id, e.thread_id, e.content, e.created_at, e.entry_type, t.title
            FROM entries e
            LEFT JOIN threads t ON e.thread_id = t.id
            WHERE e.thread_id = $threadFilter
            ORDER BY e.created_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$threadFilter", threadFilter);
    }
    else
    {
        cmd.CommandText = @"
            SELECT e.id, e.thread_id, e.content, e.created_at, e.entry_type, t.title
            FROM entries e
            LEFT JOIN threads t ON e.thread_id = t.id
            ORDER BY e.created_at DESC
            LIMIT $limit";
    }

    cmd.Parameters.AddWithValue("$limit", limit);

    var formatted = new StringBuilder();
    formatted.AppendLine("Recent Activity");
    formatted.AppendLine(new string('=', 60));
    formatted.AppendLine();

    var currentThread = "";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var id = reader.GetInt64(0);
        var threadId = reader.GetString(1);
        var content = reader.GetString(2);
        var created = reader.GetString(3);
        var type = reader.GetString(4);
        var title = reader.IsDBNull(5) ? null : reader.GetString(5);

        if (threadId != currentThread)
        {
            currentThread = threadId;
            var threadHeader = title != null ? $"{threadId} - {title}" : threadId;
            formatted.AppendLine();
            formatted.AppendLine($">>> {threadHeader}");
        }

        var preview = content.Length > 150 ? content.Substring(0, 150) + "..." : content;
        formatted.AppendLine($"[#{id}] {type} | {created}");
        formatted.AppendLine(preview);
        formatted.AppendLine();
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = formatted.ToString()
            }
        }
    };
}

JsonObject UpdateEntry(JsonObject args)
{
    var entryId = args["entry_id"]!.GetValue<long>();
    var newContent = args["new_content"]!.GetValue<string>();

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        UPDATE entries
        SET content = $content, updated_at = CURRENT_TIMESTAMP
        WHERE id = $id";
    cmd.Parameters.AddWithValue("$content", newContent);
    cmd.Parameters.AddWithValue("$id", entryId);

    var affected = cmd.ExecuteNonQuery();
    if (affected == 0)
    {
        throw new Exception($"Entry not found: {entryId}");
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"Entry #{entryId} updated successfully"
            }
        }
    };
}

JsonObject UpdateThreadMetadata(JsonObject args)
{
    var threadId = args["thread_id"]!.GetValue<string>();
    var status = args.TryGetPropertyValue("status", out var s) && s != null ? s.GetValue<string>() : null;
    var category = args.TryGetPropertyValue("category", out var c) && c != null ? c.GetValue<string>() : null;
    var title = args.TryGetPropertyValue("title", out var t) && t != null ? t.GetValue<string>() : null;

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();

    var updates = new List<string>();
    if (status != null) updates.Add("status = $status");
    if (category != null) updates.Add("category = $category");
    if (title != null) updates.Add("title = $title");

    if (updates.Count == 0)
    {
        throw new Exception("No updates specified");
    }

    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"UPDATE threads SET {string.Join(", ", updates)}, updated_at = CURRENT_TIMESTAMP WHERE id = $threadId";
    cmd.Parameters.AddWithValue("$threadId", threadId);
    if (status != null) cmd.Parameters.AddWithValue("$status", status);
    if (category != null) cmd.Parameters.AddWithValue("$category", category);
    if (title != null) cmd.Parameters.AddWithValue("$title", title);

    var affected = cmd.ExecuteNonQuery();
    if (affected == 0)
    {
        throw new Exception($"Thread not found: {threadId}");
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"Thread {threadId} updated successfully"
            }
        }
    };
}

// =========================
// FRICTION LOGGER IMPLEMENTATIONS
// =========================
JsonObject FrictionLog(JsonObject args)
{
    var description = args["description"]!.GetValue<string>();
    var category = args.TryGetPropertyValue("category", out var c) && c != null ? c.GetValue<string>() : "unspecified";
    var severity = args.TryGetPropertyValue("severity", out var s) && s != null ? s.GetValue<string>() : "medium";

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT INTO friction_log (description, category, severity, status, created_at)
        VALUES ($description, $category, $severity, 'unresolved', CURRENT_TIMESTAMP)
        RETURNING id";
    cmd.Parameters.AddWithValue("$description", description);
    cmd.Parameters.AddWithValue("$category", category);
    cmd.Parameters.AddWithValue("$severity", severity);

    var frictionId = (long)cmd.ExecuteScalar()!;

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"Friction point logged: #{frictionId}"
            }
        }
    };
}

JsonObject FrictionReview(JsonObject args)
{
    var status = args.TryGetPropertyValue("status", out var st) && st != null ? st.GetValue<string>() : "unresolved";
    var severityFilter = args.TryGetPropertyValue("severity", out var sev) && sev != null ? sev.GetValue<string>() : null;

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();

    var whereClauses = new List<string>();
    if (status != "all") whereClauses.Add("status = $status");
    if (severityFilter != null) whereClauses.Add("severity = $severity");

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, description, category, severity, status, created_at, resolved_at, solution FROM friction_log" +
        (whereClauses.Count > 0 ? " WHERE " + string.Join(" AND ", whereClauses) : "") +
        " ORDER BY severity DESC, created_at DESC";

    if (status != "all") cmd.Parameters.AddWithValue("$status", status);
    if (severityFilter != null) cmd.Parameters.AddWithValue("$severity", severityFilter);

    var formatted = new StringBuilder();
    formatted.AppendLine("Friction Log");
    formatted.AppendLine(new string('=', 60));
    formatted.AppendLine();

    var count = 0;
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        count++;
        var id = reader.GetInt64(0);
        var desc = reader.GetString(1);
        var cat = reader.GetString(2);
        var sev = reader.GetString(3);
        var stat = reader.GetString(4);
        var created = reader.GetString(5);
        var resolvedAt = reader.IsDBNull(6) ? null : reader.GetString(6);
        var solution = reader.IsDBNull(7) ? null : reader.GetString(7);

        formatted.AppendLine($"[#{id}] {sev.ToUpper()} - {cat}");
        formatted.AppendLine($"Status: {stat} | Created: {created}");
        formatted.AppendLine($"Description: {desc}");

        if (stat == "resolved" && solution != null)
        {
            formatted.AppendLine($"Resolved: {resolvedAt}");
            formatted.AppendLine($"Solution: {solution}");
        }

        formatted.AppendLine();
    }

    if (count == 0)
    {
        formatted.AppendLine("No friction points found matching criteria.");
    }
    else
    {
        formatted.Insert(0, $"Found {count} friction point(s)\n\n");
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = formatted.ToString()
            }
        }
    };
}

JsonObject FrictionResolve(JsonObject args)
{
    var frictionId = args["friction_id"]!.GetValue<long>();
    var solution = args["solution"]!.GetValue<string>();

    using var conn = new SqliteConnection(CONNECTION_STRING);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        UPDATE friction_log
        SET status = 'resolved',
            resolved_at = CURRENT_TIMESTAMP,
            solution = $solution
        WHERE id = $id";
    cmd.Parameters.AddWithValue("$solution", solution);
    cmd.Parameters.AddWithValue("$id", frictionId);

    var affected = cmd.ExecuteNonQuery();
    if (affected == 0)
    {
        throw new Exception($"Friction point not found: {frictionId}");
    }

    return new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"Friction point #{frictionId} marked as resolved"
            }
        }
    };
}

// =========================
// FINAL RUN
// =========================
Console.WriteLine($"Starting {SERVICE_NAME} v3.0 on http://0.0.0.0:5001");
Console.WriteLine($"Issuer: {ISSUER}");
Console.WriteLine($"Audience: {AUDIENCE}");

app.Run();

// =========================
// TYPE DECLARATIONS (Must be after all top-level statements)
// =========================
record AuthCode(
    string Code,
    string CodeChallenge,
    string State,
    string RedirectUri,
    DateTime ExpiresAt
);
