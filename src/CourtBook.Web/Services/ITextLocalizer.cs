namespace CourtBook.Web.Services;

public interface ITextLocalizer
{
    bool IsArabic { get; }
    string Direction { get; }
    string Lang { get; }
    string CurrentCulture { get; }

    string this[string key] { get; }
    string T(string key, params object[] args);

    string Sport(string? sport);
    string BookingStatus(string? status);
    string PaymentStatus(string? status);
    string Amenity(string? amenity);
    string SlotStatus(string? status);
    string DayOfWeek(DayOfWeek day);
    string FormatCurrency(decimal amount);
}
