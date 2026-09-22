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

        // 1. Strict Admin Portal Authorization
        if (path.StartsWith("/admin"))
        {
            var token = context.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
            {
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/Login?returnUrl={returnUrl}");
                return;
            }

            var userRole = context.Session.GetString("UserRole");
            if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/AccessDenied");
                return;
            }
        }

        // 2. Notification Center Authorization
        if (path.StartsWith("/notifications"))
        {
            var token = context.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
            {
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/Login?returnUrl={returnUrl}");
                return;
            }
        }

        // 3. General Protected Paths (Owner, Profile, Courts, Bookings, Venue Details)
        var isProtected = false;
        if (path.StartsWith("/dashboard") || 
            path.StartsWith("/owner") ||
            path.StartsWith("/profile") || 
            path.StartsWith("/courts") ||
            path.StartsWith("/venues/details") ||
            (path.StartsWith("/bookings") && !path.StartsWith("/bookings/confirmation")))
        {
            isProtected = true;
        }

        if (isProtected)
        {
            var token = context.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
            {
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/Login?returnUrl={returnUrl}");
                return;
            }

            // If accessing Owner portal, must be Owner or Admin
            if (path.StartsWith("/owner") || path.StartsWith("/dashboard"))
            {
                var userRole = context.Session.GetString("UserRole");
                if (!string.Equals(userRole, "Owner", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect("/AccessDenied");
                    return;
                }
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
