using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using CourtBook.Application.Interfaces;
using CourtBook.Application.Validators;
using CourtBook.API.Health;
using CourtBook.API.Hubs;
using CourtBook.API.Middleware;
using CourtBook.API.Services;
using CourtBook.Infrastructure.BackgroundJobs;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ─────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "CRITICAL CONFIGURATION ERROR: ConnectionStrings:DefaultConnection is not configured. " +
        "Set it via environment variable ConnectionStrings__DefaultConnection in production.");
}

if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    var lowerConn = connectionString.ToLowerInvariant();
    if (lowerConn.Contains("(localdb)") || lowerConn.Contains("localhost") || lowerConn.Contains("127.0.0.1"))
    {
        throw new InvalidOperationException(
            $"CRITICAL SECURITY ERROR: {builder.Environment.EnvironmentName} environment cannot use localhost or LocalDB database connection. " +
            "Please provide an external SQL Server instance.");
    }
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString,
        sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddScoped<ITokenService,        TokenService>();
builder.Services.AddScoped<IAuthService,         AuthService>();
builder.Services.AddScoped<IVenueService,        VenueService>();
builder.Services.AddScoped<ICourtService,        CourtService>();
builder.Services.AddScoped<IBookingService,      BookingService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<IReviewService,       ReviewService>();
builder.Services.AddScoped<IFavoriteService,     FavoriteService>();
builder.Services.AddScoped<IGameService,         GameService>();
builder.Services.AddScoped<IMatchmakingService,  MatchmakingService>();
builder.Services.AddScoped<IInvitationService,       InvitationService>();
builder.Services.AddScoped<IConnectionService,       ConnectionService>();
builder.Services.AddScoped<IPlayerCommunityService,  PlayerCommunityService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IProfileService,      ProfileService>();
builder.Services.AddScoped<IOwnerService,        OwnerService>();
builder.Services.AddScoped<IAdminVenueService,   AdminVenueService>();
builder.Services.AddScoped<ITermsService,        TermsService>();

// ── Phase 7: Payment Gateway ───────────────────────────────────────────────────
builder.Services.AddHttpClient<IPaymentGatewayService, PaymobGatewayService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IPaymentService,      PaymentService>();

// ── Phase 9.4: Settlement & Payout Services ─────────────────────────────────
builder.Services.AddScoped<ISettlementService,   SettlementService>();
builder.Services.AddScoped<IPayoutService,       PayoutService>();
builder.Services.AddScoped<IRecoveryService,     RecoveryService>();

// ── Phase 12: Commercial & Operational Features ─────────────────────────────
builder.Services.AddScoped<IPromoCodeService,     PromoCodeService>();
builder.Services.AddScoped<ICourtAddonService,     CourtAddonService>();

// ── Phase 8: Real-Time SignalR & Background Workers ──────────────────────────
var signalRBuilder = builder.Services.AddSignalR();
var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString);
}

builder.Services.AddSingleton<IRealTimeNotificationSender, SignalRNotificationSender>();
builder.Services.AddSingleton<IGameLobbySender, SignalRGameLobbySender>();
builder.Services.AddHostedService<PaymentHoldWorker>();
builder.Services.AddHostedService<SettlementWorker>();

// ── FluentValidation ──────────────────────────────────────────────────────────
// Registers all validators from the Application assembly automatically.
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// ── CORS ─────────────────────────────────────────────────────────────────────
// In production, replace with your real frontend domain via configuration.
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5100"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("PlaySpotCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    // Permissive policy for development only
    options.AddPolicy("DevCors", policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ── Rate Limiting ──────────────────────────────────────────────────────────────
// Built-in .NET 7+ middleware — no extra package needed.
builder.Services.AddRateLimiter(options =>
{
    // Strict limit on auth endpoints to slow down brute-force attacks (partitioned by remote IP)
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 10,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            }));

    // Community & anti-spam rate limiter (partitioned by user ID or remote IP)
    options.AddPolicy("community", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 30,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            }));

    // General API limiter
    options.AddFixedWindowLimiter("api", o =>
    {
        o.PermitLimit        = 120;
        o.Window             = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit         = 5;
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers["X-RateLimit-Limit"] = "120";
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
        }
        await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", cancellationToken: token);
    };

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection("JwtSettings");

var rawKey = jwtSettings["Key"];
if (string.IsNullOrWhiteSpace(rawKey))
{
    throw new InvalidOperationException(
        "CRITICAL CONFIGURATION ERROR: JwtSettings:Key is not configured. " +
        "Set it via environment variable JwtSettings__Key in production.");
}

if (rawKey.Length < 32)
{
    throw new InvalidOperationException(
        "CRITICAL SECURITY ERROR: JwtSettings:Key must be at least 32 characters (256 bits) for HMAC-SHA256.");
}

if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    var knownInsecurePlaceholders = new[]
    {
        "CourtBook_Super_Secret_Key_For_Jwt_Authentication_2024!",
        "PlaySpot_Dev_Secret_Key_Must_Be_At_Least_32_Chars_Long!",
        "CHANGE_THIS_TO_A_LONG_SECRET_KEY_32CHARS",
        "OVERRIDE_VIA_ENV_VAR_OR_PROD_SECRET_MIN_32_CHARS",
        "OVERRIDE_VIA_ENV_VAR_OR_SECRET_MIN_32_CHARS"
    };

    if (knownInsecurePlaceholders.Any(p => string.Equals(p, rawKey, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(
            $"CRITICAL SECURITY ERROR: {builder.Environment.EnvironmentName} environment cannot use a known development or placeholder JWT secret key! " +
            "Please configure a secure random 256-bit secret key via environment variable JwtSettings__Key.");
    }

    if (builder.Environment.IsProduction())
    {
        var paymobSection = builder.Configuration.GetSection("PaymentGateway:Paymob");
        var apiKey = paymobSection["ApiKey"];
        var integrationId = paymobSection["IntegrationId"];
        var iframeId = paymobSection["IframeId"];
        var hmacSecret = paymobSection["HmacSecret"];

        var invalidPlaceholders = new[]
        {
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__ApiKey",
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__IntegrationId",
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__IframeId",
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__HmacSecret"
        };

        if (string.IsNullOrWhiteSpace(apiKey) || invalidPlaceholders.Any(p => string.Equals(p, apiKey, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(integrationId) || invalidPlaceholders.Any(p => string.Equals(p, integrationId, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(iframeId) || invalidPlaceholders.Any(p => string.Equals(p, iframeId, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(hmacSecret) || invalidPlaceholders.Any(p => string.Equals(p, hmacSecret, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "CRITICAL SECURITY ERROR: Production environment cannot use missing, empty, or default placeholder Paymob gateway credentials! " +
                "Please configure valid live Paymob credentials via environment variables (PaymentGateway__Paymob__ApiKey, IntegrationId, IframeId, HmacSecret).");
        }
    }
}

var key = Encoding.UTF8.GetBytes(rawKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = jwtSettings["Issuer"],
        ValidAudience            = jwtSettings["Audience"],
        IssuerSigningKey         = new SymmetricSecurityKey(key),
        ClockSkew                = TimeSpan.FromSeconds(30), // tight clock skew
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ── API Versioning ────────────────────────────────────────────────────────────
// Strategy: URL-based versioning (/api/v{n}/...).
// Default version = 1.0. Unversioned routes also work for backward compatibility.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion  = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions  = true; // Adds api-supported-versions header
})
.AddMvc(); // Enables [ApiVersion] attribute on controllers

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck<SettlementHealthCheck>("settlement");

// ── Controllers & OpenAPI ─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Return ProblemDetails for model binding failures (matches FluentValidation output)
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            var problem = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title  = "Validation failed.",
            };
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            return new Microsoft.AspNetCore.Mvc.UnprocessableEntityObjectResult(problem);
        };
    });

// Native .NET 10 OpenAPI document
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecurityTransformer>();
});

// ── Forwarded Headers (Reverse Proxy Configuration) ─────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Security: Do NOT blindly trust arbitrary forwarded headers.
    // By default, ASP.NET Core limits KnownProxies/KnownNetworks to loopback (127.0.0.1, ::1).
    // Operators can configure trusted proxies and CIDR networks via appsettings / env vars.
    var configuredProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
    if (configuredProxies is { Length: > 0 })
    {
        foreach (var proxy in configuredProxies)
        {
            if (System.Net.IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    }

    var configuredNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
    if (configuredNetworks is { Length: > 0 })
    {
        foreach (var net in configuredNetworks)
        {
            var parts = net.Split('/');
            if (parts.Length == 2 &&
                System.Net.IPAddress.TryParse(parts[0], out var prefix) &&
                int.TryParse(parts[1], out var prefixLength))
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
            }
        }
    }
});

// ── ProblemDetails ────────────────────────────────────────────────────────────
builder.Services.AddProblemDetails();

var app = builder.Build();

// ── Seed Data: System prerequisites unconditionally, Demo data in Development ──
await SeedData.SeedAsync(app.Services, app.Environment.IsDevelopment());

// ── Middleware Pipeline ───────────────────────────────────────────────────────
// Order matters. GlobalExceptionMiddleware must be first to catch everything.
app.UseMiddleware<GlobalExceptionMiddleware>();

// Apply forwarded headers from trusted reverse proxies early before security/routing
app.UseForwardedHeaders();

var enableOpenApiInProd = builder.Configuration.GetValue<bool>("OpenApi:EnableInProduction");
if (app.Environment.IsDevelopment() || enableOpenApiInProd)
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "PlaySpot API";
        options.Authentication = new ScalarAuthenticationOptions
        {
            PreferredSecuritySchemes = new[] { "Bearer" }
        };
    });
}

// Health check endpoints — public probes for orchestration / load balancers
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true
});

// Detailed health diagnostics — restricted in production to authorized callers or local loopback
app.MapHealthChecks("/health/details", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!env.IsDevelopment())
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var requiredKey = config["HealthChecks:DetailsApiKey"];

            var providedHeaderKey = context.Request.Headers["X-Health-Key"].FirstOrDefault();
            var providedQueryKey = context.Request.Query["apiKey"].FirstOrDefault();

            var isKeyAuthorized = !string.IsNullOrWhiteSpace(requiredKey) &&
                                  (string.Equals(providedHeaderKey, requiredKey, StringComparison.Ordinal) ||
                                   string.Equals(providedQueryKey, requiredKey, StringComparison.Ordinal));

            var isLocal = context.Connection.RemoteIpAddress != null &&
                          (System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress) ||
                           context.Connection.RemoteIpAddress.Equals(context.Connection.LocalIpAddress));

            if (!isKeyAuthorized && !isLocal)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Forbidden: Detailed health diagnostics are restricted in this environment.\"}");
                return;
            }
        }

        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            timestamp = DateTime.UtcNow.ToString("o"),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                    data = entry.Value.Data
                }
            )
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await context.Response.WriteAsync(json);
    }
});

app.UseHttpsRedirection();

// CORS before auth
app.UseCors(app.Environment.IsDevelopment() ? "DevCors" : "PlaySpotCors");

app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var endpoint = context.GetEndpoint();
    var rateLimitMetadata = endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>();
    if (rateLimitMetadata != null)
    {
        context.Response.Headers["X-RateLimit-Policy"] = rateLimitMetadata.PolicyName ?? "api";
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<GameLobbyHub>("/hubs/games");

app.Run();

// ── OpenAPI Transformer ───────────────────────────────────────────────────────
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        Microsoft.OpenApi.OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
        {
            Type        = Microsoft.OpenApi.SecuritySchemeType.Http,
            Scheme      = "bearer",
            BearerFormat = "JWT",
            Description = "Enter your JWT token (without the 'Bearer ' prefix)."
        };
        return Task.CompletedTask;
    }
}
