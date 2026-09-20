namespace CourtBook.Application.Common;

/// <summary>
/// Represents a domain or application error in a structured, value-based way.
/// Use this instead of throwing exceptions for expected/business failures.
/// </summary>
public sealed record Error(string Code, string Message, int StatusCode = 400)
{
    public static readonly Error None = new(string.Empty, string.Empty, 0);

    // ── Factory helpers ──────────────────────────────────────────────────────
    public static Error NotFound(string resource) =>
        new("NOT_FOUND", $"{resource} was not found.", 404);

    public static Error Conflict(string message) =>
        new("CONFLICT", message, 409);

    public static Error Unauthorized(string message = "Authentication required.") =>
        new("UNAUTHORIZED", message, 401);

    public static Error Forbidden(string message = "You do not have permission to perform this action.") =>
        new("FORBIDDEN", message, 403);

    public static Error Validation(string message) =>
        new("VALIDATION_ERROR", message, 422);

    public static Error Internal(string message = "An unexpected error occurred. Please try again.") =>
        new("INTERNAL_ERROR", message, 500);

    public static Error BadRequest(string message) =>
        new("BAD_REQUEST", message, 400);

    public static Error Expired(string message = "The resource has expired.") =>
        new("EXPIRED", message, 410);
}
