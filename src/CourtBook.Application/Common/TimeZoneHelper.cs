namespace CourtBook.Application.Common;

/// <summary>
/// Provides cross-platform Egyptian timezone conversions for operating schedules,
/// availability calculation, and booking reservation timestamps.
/// Venue operating hours represent local Egyptian time, while database timestamps are stored in UTC.
/// </summary>
public static class TimeZoneHelper
{
    private static readonly Lazy<TimeZoneInfo> _egyptTz = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
            }
            catch
            {
                // Resilient fallback fixed +02:00 if system timezone registry is missing
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Egypt Standard Time",
                    TimeSpan.FromHours(2),
                    "Egypt Standard Time",
                    "Egypt Standard Time");
            }
        }
    });

    /// <summary>
    /// The official Egypt timezone instance (UTC+2 standard, UTC+3 during DST).
    /// </summary>
    public static TimeZoneInfo EgyptTimeZone => _egyptTz.Value;

    /// <summary>
    /// Gets the current local Egyptian DateTime.
    /// </summary>
    public static DateTime GetCurrentEgyptTime()
    {
        return ConvertUtcToEgypt(DateTime.UtcNow);
    }

    /// <summary>
    /// Converts a UTC DateTime to local Egyptian DateTime.
    /// </summary>
    public static DateTime ConvertUtcToEgypt(DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, EgyptTimeZone);
    }

    /// <summary>
    /// Converts a local Egyptian DateTime to UTC DateTime.
    /// </summary>
    public static DateTime ConvertEgyptToUtc(DateTime egyptLocalDateTime)
    {
        var unspecified = DateTime.SpecifyKind(egyptLocalDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, EgyptTimeZone);
    }

    /// <summary>
    /// Creates a UTC DateTime given a local Egyptian DateOnly and TimeOnly.
    /// </summary>
    public static DateTime CreateUtcFromEgyptDateAndTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);
        return ConvertEgyptToUtc(localDateTime);
    }

    /// <summary>
    /// Computes the precise UTC interval spanning a 24-hour Egyptian calendar day.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) GetEgyptDayUtcRange(DateOnly date)
    {
        var startUtc = CreateUtcFromEgyptDateAndTime(date, TimeOnly.MinValue);
        var endUtc = CreateUtcFromEgyptDateAndTime(date.AddDays(1), TimeOnly.MinValue);
        return (startUtc, endUtc);
    }
}
