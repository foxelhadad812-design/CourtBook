using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SportType
{
    Football,
    Padel,
    Tennis,
    Basketball,
    Volleyball,
    Badminton
}
