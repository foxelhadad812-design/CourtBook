using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Bookings;

public class ConfirmationModel : PageModel
{
    private readonly ApiClient _api;

    public ConfirmationModel(ApiClient api)
    {
        _api = api;
    }

    public string? BookingId { get; set; }
    public string? BookingReference { get; set; }
    public string? CourtName { get; set; }
    public string? SportType { get; set; }
    public string? VenueName { get; set; }
    public string? VenueCity { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? TotalPrice { get; set; }
    public string? PaymentStatus { get; set; }
    public string? GoogleMapsUrl { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? PromoCode { get; set; }
    public List<BookingAddonDto> Addons { get; set; } = [];

    public string GetWhatsAppShareUrl(bool isArabic)
    {
        var dateStr = StartTime.ToString("yyyy-MM-dd");
        var timeStr = $"{StartTime:hh:mm tt} - {EndTime:hh:mm tt}";
        string text;
        if (isArabic)
        {
            text = $"🎾 *تفاصيل حجز ملعب - PlaySpot Egypt*\n\n" +
                   $"📍 *المنشأة:* {VenueName} ({VenueCity})\n" +
                   $"🏟️ *الملعب:* {CourtName} ({SportType})\n" +
                   $"📅 *التاريخ:* {dateStr}\n" +
                   $"⏰ *الوقت:* {timeStr}\n" +
                   $"🏷️ *كود الحجز:* `{BookingReference}`\n" +
                   (!string.IsNullOrEmpty(GoogleMapsUrl) ? $"🗺️ *الموقع على الخريطة:* {GoogleMapsUrl}\n" : "") +
                   $"\nجاهزون للماتش! ⚽🔥";
        }
        else
        {
            text = $"🎾 *Match Reservation - PlaySpot Egypt*\n\n" +
                   $"📍 *Venue:* {VenueName} ({VenueCity})\n" +
                   $"🏟️ *Court:* {CourtName} ({SportType})\n" +
                   $"📅 *Date:* {dateStr}\n" +
                   $"⏰ *Time:* {timeStr}\n" +
                   $"🏷️ *Booking Ref:* `{BookingReference}`\n" +
                   (!string.IsNullOrEmpty(GoogleMapsUrl) ? $"🗺️ *Location:* {GoogleMapsUrl}\n" : "") +
                   $"\nReady for the game! ⚽🔥";
        }
        return $"https://wa.me/?text={Uri.EscapeDataString(text)}";
    }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        // 1. Try reading from TempData first
        if (TempData["BookingId"] != null)
        {
            BookingId = TempData["BookingId"]?.ToString();
            BookingReference = TempData["BookingReference"]?.ToString() ?? $"PS-{DateTime.UtcNow:yyyyMMdd}-RES";
            CourtName = TempData["CourtName"]?.ToString() ?? "Sports Court";
            SportType = TempData["SportType"]?.ToString() ?? "Sports";
            VenueName = TempData["VenueName"]?.ToString() ?? "PlaySpot Facility";
            VenueCity = TempData["VenueCity"]?.ToString() ?? "Egypt";
            TotalPrice = TempData["TotalPrice"]?.ToString() ?? "0.00";
            PaymentStatus = TempData["PaymentStatus"]?.ToString() ?? "Pending";
            GoogleMapsUrl = TempData["GoogleMapsUrl"]?.ToString();
            PromoCode = TempData["PromoCode"]?.ToString();
            if (decimal.TryParse(TempData["DiscountAmount"]?.ToString(), out var da)) DiscountAmount = da;

            if (DateTime.TryParse(TempData["StartTime"]?.ToString(), out var st)) StartTime = st;
            if (DateTime.TryParse(TempData["EndTime"]?.ToString(), out var et)) EndTime = et;

            return Page();
        }

        // 2. If TempData expired but ID is provided in query string, fetch from API
        if (id.HasValue && id.Value != Guid.Empty)
        {
            try
            {
                var resp = await _api.Client.GetAsync($"/api/bookings/{id.Value}");
                if (resp.IsSuccessStatusCode)
                {
                    var b = await resp.Content.ReadFromJsonAsync<BookingResponse>();
                    if (b != null)
                    {
                        BookingId = b.Id.ToString();
                        BookingReference = b.BookingReference;
                        CourtName = b.CourtName;
                        SportType = b.SportType;
                        VenueName = b.VenueName;
                        VenueCity = b.VenueCity;
                        StartTime = b.StartTime;
                        EndTime = b.EndTime;
                        TotalPrice = b.TotalPrice.ToString("0.00");
                        PaymentStatus = b.PaymentStatus;
                        GoogleMapsUrl = b.GoogleMapsUrl;
                        DiscountAmount = b.DiscountAmount;
                        PromoCode = b.PromoCode;
                        Addons = b.Addons ?? [];
                        return Page();
                    }
                }
            }
            catch
            {
                // Fall back to redirect
            }
        }

        // If no booking data available, redirect to user's bookings
        return RedirectToPage("/Bookings/MyBookings");
    }
}
