using CourtBook.Application.Common;
using Xunit;

namespace CourtBook.Tests;

public class EgyptTimeZoneTests
{
    [Fact]
    public void EgyptTimeZone_IsResolvedCorrectly()
    {
        var tz = TimeZoneHelper.EgyptTimeZone;
        Assert.NotNull(tz);
        // Base UTC offset for Cairo is at least 2 hours
        Assert.True(tz.BaseUtcOffset >= TimeSpan.FromHours(2));
    }

    [Fact]
    public void ConvertUtcToEgypt_ConvertsAccurately()
    {
        // 12:00 UTC
        var utcTime = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        var egyptTime = TimeZoneHelper.ConvertUtcToEgypt(utcTime);

        // In July, Egypt is on daylight saving time (UTC+3)
        // 12:00 UTC -> 15:00 Cairo
        Assert.Equal(15, egyptTime.Hour);
        Assert.Equal(0, egyptTime.Minute);
    }

    [Fact]
    public void CreateUtcFromEgyptDateAndTime_ConvertsLocalToUtcAccurately()
    {
        var date = new DateOnly(2026, 7, 15);
        var time = new TimeOnly(18, 0); // 18:00 Egypt time

        var utcDateTime = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(date, time);

        // In July (UTC+3), 18:00 Egypt time is 15:00 UTC
        Assert.Equal(DateTimeKind.Utc, utcDateTime.Kind);
        Assert.Equal(15, utcDateTime.Hour);
        Assert.Equal(0, utcDateTime.Minute);
    }

    [Fact]
    public void GetEgyptDayUtcRange_ReturnsExact24HourSpan()
    {
        var date = new DateOnly(2026, 10, 10);
        var (startUtc, endUtc) = TimeZoneHelper.GetEgyptDayUtcRange(date);

        Assert.Equal(DateTimeKind.Utc, startUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, endUtc.Kind);
        Assert.True(endUtc > startUtc);
        Assert.Equal(TimeSpan.FromHours(24), endUtc - startUtc);
    }
}
