using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Payments;

public class CancelledModel : PageModel
{
    public Guid? BookingId { get; set; }

    public void OnGet(Guid? bookingId)
    {
        BookingId = bookingId;
    }
}
