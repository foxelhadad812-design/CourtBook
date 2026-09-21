using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

/// <summary>
/// Status of an owner facility in the administrative verification pipeline.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VenueApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
