using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameStatus
{
    Open,
    Full,
    InProgress,
    Completed,
    Cancelled
}
