namespace CourtBook.Domain.Entities;

public class PromoCodeUsage
{
    public Guid Id { get; set; }
    public Guid PromoCodeId { get; set; }
    public Guid UserId { get; set; }
    public Guid BookingId { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;

    public PromoCode PromoCode { get; set; } = null!;
    public User User { get; set; } = null!;
    public Booking Booking { get; set; } = null!;
}
