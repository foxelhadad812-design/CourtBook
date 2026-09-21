using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using CourtBook.Application.Interfaces;
using CourtBook.Application.Validators;
using CourtBook.API.Middleware;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

if (builder.Environment.IsProduction())
{
    var lowerConn = connectionString.ToLowerInvariant();
    if (lowerConn.Contains("(localdb)") || lowerConn.Contains("localhost") || lowerConn.Contains("127.0.0.1"))
    {
        throw new InvalidOperationException(
            "CRITICAL SECURITY ERROR: Production environment cannot use localhost or LocalDB database connection. " +
            "Please provide a production SQL Server instance.");
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
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IProfileService,      ProfileService>();
builder.Services.AddScoped<IOwnerService,        OwnerService>();
builder.Services.AddScoped<IAdminVenueService,   AdminVenueService>();
builder.Services.AddScoped<ITermsService,        TermsService>();

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
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// ── Rate Limiting ──────────────────────────────────────────────────────────────
// Built-in .NET 7+ middleware — no extra package needed.
builder.Services.AddRateLimiter(options =>
{
    // Strict limit on auth endpoints to slow down brute-force attacks
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit        = 5;
        o.Window             = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit         = 0;
    });

    // General API limiter
    options.AddFixedWindowLimiter("api", o =>
    {
        o.PermitLimit        = 120;
        o.Window             = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit         = 5;
    });

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

if (builder.Environment.IsProduction())
{
    var knownInsecurePlaceholders = new[]
    {
        "CourtBook_Super_Secret_Key_For_Jwt_Authentication_2024!",
        "PlaySpot_Dev_Secret_Key_Must_Be_At_Least_32_Chars_Long!",
        "CHANGE_THIS_TO_A_LONG_SECRET_KEY_32CHARS",
        "OVERRIDE_VIA_ENV_VAR_OR_PROD_SECRET_MIN_32_CHARS"
    };

    if (knownInsecurePlaceholders.Any(p => string.Equals(p, rawKey, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(
            "CRITICAL SECURITY ERROR: Production environment cannot use a known development or placeholder JWT secret key! " +
            "Please configure a secure random 256-bit secret key via environment variable JwtSettings__Key.");
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
    .AddDbContextCheck<AppDbContext>("database");

// ── Controllers & OpenAPI ─────────────────────────────────────────────────────
builder.Services.AddControllers()
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

// ── ProblemDetails ────────────────────────────────────────────────────────────
builder.Services.AddProblemDetails();

var app = builder.Build();

// ── Seed Data: System prerequisites unconditionally, Demo data in Development ──
await SeedData.SeedAsync(app.Services, app.Environment.IsDevelopment());

// ── Middleware Pipeline ───────────────────────────────────────────────────────
// Order matters. GlobalExceptionMiddleware must be first to catch everything.
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
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

// Health check endpoint — no auth required
app.MapHealthChecks("/health");

app.UseHttpsRedirection();

// CORS before auth
app.UseCors(app.Environment.IsDevelopment() ? "DevCors" : "PlaySpotCors");

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
