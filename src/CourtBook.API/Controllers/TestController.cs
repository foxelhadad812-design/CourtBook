#if DEBUG
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    /// <summary>
    /// Public endpoint — no token required.
    /// Use this to confirm the API is reachable.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult Public()
        => Ok("✅ Public endpoint — no auth needed.");

    /// <summary>
    /// Protected endpoint — requires a valid JWT with the Admin role.
    /// Returns 401 if no token is provided, 403 if the role doesn't match.
    /// </summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminOnly()
        => Ok($"✅ Hello, Admin! Authenticated as: {User.Identity?.Name}");
}
#endif
