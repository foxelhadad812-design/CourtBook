using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayoutStatus
{
    Submitted,
    Approved,
    Paid,
    Rejected,
    Cancelled,
    Reversed
}
