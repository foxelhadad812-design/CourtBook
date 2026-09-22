using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Payments;

public class FailedModel : PageModel
{
    public Guid? BookingId { get; set; }
    public string? Reason { get; set; }

    public void OnGet(Guid? bookingId, string? reason)
    {
        BookingId = bookingId;
        Reason    = reason;
    }
}
