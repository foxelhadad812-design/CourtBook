using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

/// <summary>
/// Type of platform terms document.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TermsType
{
    Player = 0,
    FacilityOwner = 1
}
