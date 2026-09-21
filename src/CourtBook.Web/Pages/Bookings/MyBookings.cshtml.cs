using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Bookings;

public class MyBookingsModel : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("/Bookings/Index");
    }
}
