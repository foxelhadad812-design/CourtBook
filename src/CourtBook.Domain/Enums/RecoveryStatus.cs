using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecoveryStatus
{
    Active,
    Recovered,
    WrittenOff
}
