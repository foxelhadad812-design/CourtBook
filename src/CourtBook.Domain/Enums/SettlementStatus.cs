using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettlementStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
