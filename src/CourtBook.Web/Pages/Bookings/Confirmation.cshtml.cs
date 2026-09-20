using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Bookings;

public class ConfirmationModel : PageModel
{
    public string? BookingId { get; set; }
    public string? CourtName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? TotalPrice { get; set; }

    public IActionResult OnGet()
    {
        if (TempData["BookingId"] == null)
        {
            return RedirectToPage("/Bookings/MyBookings");
        }

        BookingId = TempData["BookingId"]?.ToString();
        CourtName = TempData["CourtName"]?.ToString();
        TotalPrice = TempData["TotalPrice"]?.ToString();
        
        if (DateTime.TryParse(TempData["StartTime"]?.ToString(), out var st)) StartTime = st;
        if (DateTime.TryParse(TempData["EndTime"]?.ToString(), out var et)) EndTime = et;

        return Page();
    }
}
