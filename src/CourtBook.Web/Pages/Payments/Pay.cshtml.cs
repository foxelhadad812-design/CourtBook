using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Payments;

/// <summary>
/// Payment selection page — player selects payment method after booking.
/// Amount is fetched from server (never trusts browser-provided price).
/// </summary>
public class PayModel : PageModel
{
    private readonly ApiClient _api;

    public PayModel(ApiClient api) => _api = api;

    public Guid BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string CourtName { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? bookingId)
    {
        if (!bookingId.HasValue || bookingId.Value == Guid.Empty)
            return RedirectToPage("/Bookings/Index");

        BookingId = bookingId.Value;

        try
        {
            var resp = await _api.Client.GetAsync($"/api/bookings/{bookingId}");
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return RedirectToPage("/Login", new { returnUrl = $"/Payments/Pay?bookingId={bookingId}" });

            if (!resp.IsSuccessStatusCode)
                return RedirectToPage("/Bookings/Index");

            var booking = await resp.Content.ReadFromJsonAsync<BookingResponse>();
            if (booking is null)
                return RedirectToPage("/Bookings/Index");

            BookingReference = booking.BookingReference;
            CourtName        = booking.CourtName;
            VenueName        = booking.VenueName;
            TotalPrice       = booking.TotalPrice;
            StartTime        = booking.StartTime;
            EndTime          = booking.EndTime;

            // If already paid, redirect to confirmation
            if (booking.PaymentStatus is "Completed")
                return RedirectToPage("/Bookings/Confirmation", new { id = bookingId });
        }
        catch
        {
            ErrorMessage = "Unable to load booking details. Please try again.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid bookingId, string paymentMethod)
    {
        try
        {
            var payload = new { bookingId, paymentMethod };
            var resp    = await _api.Client.PostAsJsonAsync("/api/payments/initiate", payload);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return RedirectToPage("/Login", new { returnUrl = $"/Payments/Pay?bookingId={bookingId}" });

            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<InitiatePaymentResponse>();
                if (result is null)
                {
                    ErrorMessage = "Payment could not be initiated.";
                    return await OnGetAsync(bookingId);
                }

                // PayAtFacility — no redirect needed
                if (string.Equals(paymentMethod, "PayAtFacility", StringComparison.OrdinalIgnoreCase))
                    return RedirectToPage("/Bookings/Confirmation", new { id = bookingId });

                // Online payment — redirect to gateway
                if (!string.IsNullOrWhiteSpace(result.PaymentUrl))
                    return Redirect(result.PaymentUrl);

                ErrorMessage = "Payment URL not available.";
                return await OnGetAsync(bookingId);
            }

            var err = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            ErrorMessage = err?.GetValueOrDefault("error") ?? "Payment initiation failed.";
        }
        catch
        {
            ErrorMessage = "Network error during payment.";
        }

        return await OnGetAsync(bookingId);
    }
}
