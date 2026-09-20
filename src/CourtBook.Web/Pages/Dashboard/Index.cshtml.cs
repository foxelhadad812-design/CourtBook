using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Dashboard;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;
    public IndexModel(ApiClient api) => _api = api;

    public List<VenueResponse> Venues { get; set; } = [];

    public async Task OnGetAsync()
    {
        // For simplicity we just fetch all venues and filter by the owner's userId from the backend, 
        // wait, the API GET /api/venues returns ALL active venues. 
        // We don't have a GET /api/venues/my endpoint for owners.
        // We can just get all venues and filter by OwnerId if we had it in session.
        // Let's check API VenuesController: GetAll returns all venues.
        var allVenues = await _api.Client.GetFromJsonAsync<List<VenueResponse>>("/api/venues");
        if (allVenues != null)
        {
            // We need OwnerId. But we didn't save OwnerId in session. We only saved email.
            // Wait! The user is an Owner. How do we filter?
            // Since we don't have /my venues endpoint, I'll add an endpoint or fetch the user id.
            // Let's decode the JWT token to get the user ID!
            var token = HttpContext.Session.GetString("JwtToken");
            if (!string.IsNullOrEmpty(token))
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                var sub = jwt.Claims.FirstOrDefault(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
                if (Guid.TryParse(sub, out var userId))
                {
                    Venues = allVenues.Where(v => v.OwnerId == userId).ToList();
                }
            }
        }
    }
}
