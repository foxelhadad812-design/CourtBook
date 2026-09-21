using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace CourtBook.Web.Services;

/// <summary>
/// Attaches the user's JWT Bearer token from the current session to outgoing HTTP requests
/// without mutating shared HttpClient DefaultRequestHeaders.
/// </summary>
public class SessionTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionTokenHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var token = httpContext?.Session?.GetString("JwtToken");

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
