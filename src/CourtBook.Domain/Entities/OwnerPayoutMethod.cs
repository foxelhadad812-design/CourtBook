using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Registered disbursement destination for an owner (Bank, InstaPay, Mobile Wallet).
/// Uses soft-deactivation (IsActive = false) to preserve foreign key history.
/// </summary>
public class OwnerPayoutMethod
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public PayoutMethodType Type { get; set; }
    public string AccountHolderName { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public string? Iban { get; set; }
    public string? AccountNumber { get; set; }
    public string? InstaPayAddress { get; set; }
    public string? MobileWalletNumber { get; set; }
    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public User Owner { get; set; } = null!;
    public ICollection<PayoutRequest> PayoutRequests { get; set; } = new List<PayoutRequest>();
}
