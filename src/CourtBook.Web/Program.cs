using CourtBook.Web.Middleware;
using CourtBook.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddHttpContextAccessor();

// Cache & Session State (defaults to memory; cluster deployments can swap in AddStackExchangeRedisCache)
var webRedisConnectionString = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(webRedisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = webRedisConnectionString;
        options.InstanceName = "CourtBookWeb_";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction() || builder.Environment.IsStaging()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddSingleton<ITextLocalizer, TextLocalizer>();

builder.Services.AddTransient<SessionTokenHandler>();

var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5257";
if ((builder.Environment.IsProduction() || builder.Environment.IsStaging()) && string.IsNullOrWhiteSpace(builder.Configuration["ApiSettings:BaseUrl"]))
{
    throw new InvalidOperationException($"CRITICAL CONFIGURATION ERROR: ApiSettings:BaseUrl must be explicitly configured in {builder.Environment.EnvironmentName}.");
}

builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
}).AddHttpMessageHandler<SessionTokenHandler>();

var app = builder.Build();

// Configure localization
var supportedCultures = new[] { "en", "ar" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
app.UseStatusCodePagesWithReExecute("/NotFound");

app.UseRouting();

app.UseSession();
app.UseAuthMiddleware();

app.UseAuthorization();

// API forwarding routes for browser client calls (Terms modal, Smart Assistant, public discovery)
app.MapGet("/api/terms/{type}", async (string type, ApiClient api) =>
{
    var resp = await api.Client.GetAsync($"/api/terms/{type}");
    if (!resp.IsSuccessStatusCode)
    {
        return Results.NotFound(new { message = "Terms document not found." });
    }
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json");
}).AllowAnonymous();

app.MapGet("/api/venues/{**path}", async (string? path, HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var targetUrl = $"/api/venues/{path}{query}";
    var resp = await api.Client.GetAsync(targetUrl);
    var content = await resp.Content.ReadAsStringAsync();
    return Results.Content(content, resp.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)resp.StatusCode);
}).AllowAnonymous();

app.MapGet("/api/games/{**path}", async (string? path, HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var targetUrl = $"/api/games/{path}{query}";
    var resp = await api.Client.GetAsync(targetUrl);
    var content = await resp.Content.ReadAsStringAsync();
    return Results.Content(content, resp.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)resp.StatusCode);
}).AllowAnonymous();

// Forwarding for browser client notifications
app.MapGet("/api/notifications/unread-count", async (ApiClient api) =>
{
    var resp = await api.Client.GetAsync("/api/notifications/unread-count");
    var content = await resp.Content.ReadAsStringAsync();
    return Results.Content(content, resp.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/notifications", async (HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var resp = await api.Client.GetAsync($"/api/notifications{query}");
    var content = await resp.Content.ReadAsStringAsync();
    return Results.Content(content, resp.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/notifications/{id}/read", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.PostAsync($"/api/notifications/{id}/read", null);
    return Results.StatusCode((int)resp.StatusCode);
});

app.MapPost("/api/notifications/read-all", async (ApiClient api) =>
{
    var resp = await api.Client.PostAsync("/api/notifications/read-all", null);
    return Results.StatusCode((int)resp.StatusCode);
});

// Phase 7: Payment forwarding routes
app.MapPost("/api/payments/initiate", async (HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync("/api/payments/initiate", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/payments/verify", async (string orderId, ApiClient api) =>
{
    var resp = await api.Client.GetAsync($"/api/payments/verify?orderId={Uri.EscapeDataString(orderId)}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/payments/booking/{bookingId:guid}", async (Guid bookingId, ApiClient api) =>
{
    var resp = await api.Client.GetAsync($"/api/payments/booking/{bookingId}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/payments/owner/report", async (HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var resp  = await api.Client.GetAsync($"/api/payments/owner/report{query}");
    var json  = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/payments/admin/transactions", async (HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var resp  = await api.Client.GetAsync($"/api/payments/admin/transactions{query}");
    var json  = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

// Phase 9.5: Owner Financial Portal Forwarding
app.MapGet("/api/owner/balance", async (ApiClient api) =>
{
    var resp = await api.Client.GetAsync("/api/owner/balance");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/owner/payout-methods", async (ApiClient api) =>
{
    var resp = await api.Client.GetAsync("/api/owner/payout-methods");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/owner/payout-methods", async (HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync("/api/owner/payout-methods", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapDelete("/api/owner/payout-methods/{id:guid}", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.DeleteAsync($"/api/owner/payout-methods/{id}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/owner/payout-methods/{id:guid}/default", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.PostAsync($"/api/owner/payout-methods/{id}/default", null);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/owner/payouts", async (HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var resp = await api.Client.GetAsync($"/api/owner/payouts{query}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/owner/payouts/request", async (HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync("/api/owner/payouts/request", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/owner/payouts/{id:guid}", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.GetAsync($"/api/owner/payouts/{id}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/owner/payouts/{id:guid}/cancel", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.PostAsync($"/api/owner/payouts/{id}/cancel", null);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

// Phase 9.5: Admin Settlements Forwarding
app.MapGet("/api/admin/settlements/summary", async (ApiClient api) =>
{
    var resp = await api.Client.GetAsync("/api/admin/settlements/summary");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/admin/settlements", async (HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var resp = await api.Client.GetAsync($"/api/admin/settlements{query}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/admin/settlements/{id:guid}", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.GetAsync($"/api/admin/settlements/{id}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/admin/settlements/run", async (HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync("/api/admin/settlements/run", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

// Phase 9.5: Admin Payouts Forwarding
app.MapGet("/api/admin/payouts/summary", async (ApiClient api) =>
{
    var resp = await api.Client.GetAsync("/api/admin/payouts/summary");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/admin/payouts", async (HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var resp = await api.Client.GetAsync($"/api/admin/payouts{query}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/admin/payouts/{id:guid}", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.GetAsync($"/api/admin/payouts/{id}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/admin/payouts/{id:guid}/approve", async (Guid id, ApiClient api) =>
{
    var resp = await api.Client.PostAsync($"/api/admin/payouts/{id}/approve", null);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/admin/payouts/{id:guid}/reject", async (Guid id, HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync($"/api/admin/payouts/{id}/reject", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/admin/payouts/{id:guid}/mark-paid", async (Guid id, HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync($"/api/admin/payouts/{id}/mark-paid", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

// Phase 9.5: Admin Recovery Obligations Forwarding
app.MapGet("/api/admin/financial/recovery-obligations/summary", async (ApiClient api) =>
{
    var resp = await api.Client.GetAsync("/api/admin/financial/recovery-obligations/summary");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapGet("/api/admin/financial/recovery-obligations", async (HttpContext ctx, ApiClient api) =>
{
    var query = ctx.Request.QueryString.Value ?? "";
    var resp = await api.Client.GetAsync($"/api/admin/financial/recovery-obligations{query}");
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/admin/financial/recovery-obligations/{id:guid}/settle-manually", async (Guid id, HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync($"/api/admin/financial/recovery-obligations/{id}/settle-manually", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/api/admin/financial/recovery-obligations/{id:guid}/write-off", async (Guid id, HttpContext ctx, ApiClient api) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await api.Client.PostAsync($"/api/admin/financial/recovery-obligations/{id}/write-off", content);
    var json = await resp.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
