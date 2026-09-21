using Microsoft.AspNetCore.Http;

namespace CourtBook.Web.Services;

/// <summary>
/// Provides access to the pre-configured HttpClient for API communication.
/// Authentication headers are attached per-request by SessionTokenHandler.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient, IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpClient = httpClient;
    }

    public HttpClient Client => _httpClient;
}
