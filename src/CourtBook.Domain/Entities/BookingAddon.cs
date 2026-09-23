namespace CourtBook.Domain.Entities;

public class BookingAddon
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid CourtAddonId { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

    public Booking Booking { get; set; } = null!;
    public CourtAddon CourtAddon { get; set; } = null!;
}
