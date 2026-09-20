namespace CourtBook.Web.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // List of paths that require authentication
        var protectedPaths = new[] { "/dashboard", "/courts", "/my-bookings" };
        
        // Wait, courts/{id} is public in API? Wait, the requirement says:
        // /courts/{id}/book -> Client only
        // dashboard -> Owner only
        // my-bookings -> Client only

        var isProtected = false;
        if (path.StartsWith("/dashboard") || path.StartsWith("/bookings/mybookings") || path.StartsWith("/courts/book"))
        {
            isProtected = true;
        }

        if (isProtected)
        {
            var token = context.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
            {
                context.Response.Redirect("/Login");
                return;
            }
        }

        await _next(context);
    }
}

public static class AuthMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthMiddleware>();
    }
}
