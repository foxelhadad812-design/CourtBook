using System.Text.Json.Serialization;

namespace CourtBook.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2
}
