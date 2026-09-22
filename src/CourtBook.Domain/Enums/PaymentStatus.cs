namespace CourtBook.Domain.Enums;

public enum PaymentStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Refunded,
    PartiallyRefunded,
    Cancelled
}
