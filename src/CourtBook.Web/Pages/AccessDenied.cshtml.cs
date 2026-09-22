using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
        Response.StatusCode = 403;
    }
}
