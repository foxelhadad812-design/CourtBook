using CourtBook.Web.Middleware;
using CourtBook.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddHttpContextAccessor();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddSingleton<ITextLocalizer, TextLocalizer>();

builder.Services.AddTransient<SessionTokenHandler>();

var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5257";
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(builder.Configuration["ApiSettings:BaseUrl"]))
{
    throw new InvalidOperationException("CRITICAL CONFIGURATION ERROR: ApiSettings:BaseUrl must be explicitly configured in Production.");
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

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
