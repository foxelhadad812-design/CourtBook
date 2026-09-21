using CourtBook.Domain.Enums;

namespace CourtBook.Application.DTOs;

public class AdminVenueDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public VenueApprovalStatus ApprovalStatus { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsActive { get; set; }
    public int TotalCourts { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RejectVenueRequest
{
    public string Reason { get; set; } = string.Empty;
}
