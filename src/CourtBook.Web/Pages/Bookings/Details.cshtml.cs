using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Bookings;

public class DetailsModel : PageModel
{
    private readonly ApiClient _api;

    public DetailsModel(ApiClient api)
    {
        _api = api;
    }

    public BookingResponse? Booking { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsNotFound { get; set; }
    public bool IsForbidden { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public string GetWhatsAppShareUrl(bool isArabic)
    {
        if (Booking == null) return "#";
        var dateStr = Booking.StartTime.ToString("yyyy-MM-dd");
        var timeStr = $"{Booking.StartTime:hh:mm tt} - {Booking.EndTime:hh:mm tt}";
        string text;
        if (isArabic)
        {
            text = $"🎾 *تفاصيل حجز ملعب - PlaySpot Egypt*\n\n" +
                   $"📍 *المنشأة:* {Booking.VenueName} ({Booking.VenueCity})\n" +
                   $"🏟️ *الملعب:* {Booking.CourtName} ({Booking.SportType})\n" +
                   $"📅 *التاريخ:* {dateStr}\n" +
                   $"⏰ *الوقت:* {timeStr}\n" +
                   $"🏷️ *كود الحجز:* `{Booking.BookingReference}`\n" +
                   (!string.IsNullOrEmpty(Booking.GoogleMapsUrl) ? $"🗺️ *الموقع على الخريطة:* {Booking.GoogleMapsUrl}\n" : "") +
                   $"\nجاهزون للماتش! ⚽🔥";
        }
        else
        {
            text = $"🎾 *Match Reservation - PlaySpot Egypt*\n\n" +
                   $"📍 *Venue:* {Booking.VenueName} ({Booking.VenueCity})\n" +
                   $"🏟️ *Court:* {Booking.CourtName} ({Booking.SportType})\n" +
                   $"📅 *Date:* {dateStr}\n" +
                   $"⏰ *Time:* {timeStr}\n" +
                   $"🏷️ *Booking Ref:* `{Booking.BookingReference}`\n" +
                   (!string.IsNullOrEmpty(Booking.GoogleMapsUrl) ? $"🗺️ *Location:* {Booking.GoogleMapsUrl}\n" : "") +
                   $"\nReady for the game! ⚽🔥";
        }
        return $"https://wa.me/?text={Uri.EscapeDataString(text)}";
    }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (!id.HasValue || id.Value == Guid.Empty)
        {
            return RedirectToPage("/Bookings/Index");
        }

        try
        {
            var resp = await _api.Client.GetAsync($"/api/bookings/{id.Value}");

            if (resp.IsSuccessStatusCode)
            {
                Booking = await resp.Content.ReadFromJsonAsync<BookingResponse>();
                return Page();
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                IsNotFound = true;
                return Page();
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                IsForbidden = true;
                return Page();
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return RedirectToPage("/Login", new { returnUrl = $"/Bookings/Details?id={id.Value}" });
            }

            ErrorMessage = "Failed to load booking details.";
        }
        catch
        {
            ErrorMessage = "Network error connecting to booking service.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, [FromForm] string? reason)
    {
        try
        {
            var payload = new CancelBookingRequest { Reason = reason };
            var resp = await _api.Client.PostAsJsonAsync($"/api/bookings/{id}/cancel", payload);

            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<CancelBookingResult>();
                SuccessMessage = result?.Message ?? "Booking cancelled successfully.";
            }
            else
            {
                var errorObj = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                ErrorMessage = errorObj != null && errorObj.TryGetValue("error", out var msg)
                    ? msg
                    : "Unable to cancel booking.";
            }
        }
        catch
        {
            ErrorMessage = "Network failure while cancelling booking.";
        }

        return RedirectToPage(new { id });
    }
}
