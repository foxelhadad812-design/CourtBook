using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Payments;

/// <summary>
/// Return page after successful gateway redirect.
/// Server re-verifies payment status — never trusts browser alone.
/// </summary>
public class SuccessModel : PageModel
{
    private readonly ApiClient _api;

    public SuccessModel(ApiClient api) => _api = api;

    public bool PaymentConfirmed { get; set; }
    public string? BookingReference { get; set; }
    public decimal Amount { get; set; }
    public string? TransactionReference { get; set; }
    public Guid BookingId { get; set; }

    public async Task<IActionResult> OnGetAsync([FromQuery] string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return RedirectToPage("/Bookings/Index");

        try
        {
            // Server-side verification — do NOT trust this URL alone
            var resp = await _api.Client.GetAsync($"/api/payments/verify?orderId={Uri.EscapeDataString(orderId)}");
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<PaymentVerificationResult>();
                if (result is not null)
                {
                    PaymentConfirmed     = result.IsSuccessful;
                    Amount               = result.Amount;
                    TransactionReference = result.TransactionReference;
                    BookingId            = result.BookingId;

                    // Fetch booking reference
                    if (BookingId != Guid.Empty)
                    {
                        var bookingResp = await _api.Client.GetAsync($"/api/bookings/{BookingId}");
                        if (bookingResp.IsSuccessStatusCode)
                        {
                            var booking = await bookingResp.Content.ReadFromJsonAsync<BookingResponse>();
                            BookingReference = booking?.BookingReference;
                        }
                    }
                }
            }
        }
        catch
        {
            PaymentConfirmed = false;
        }

        return Page();
    }

    // Minimal DTO for deserialization
    private class PaymentVerificationResult
    {
        public bool IsSuccessful { get; set; }
        public Guid BookingId { get; set; }
        public string? TransactionReference { get; set; }
        public decimal Amount { get; set; }
        public string? Status { get; set; }
    }

    private class BookingResponse
    {
        public string BookingReference { get; set; } = string.Empty;
    }
}
