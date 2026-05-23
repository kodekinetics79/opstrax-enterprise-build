using System.Globalization;

namespace Zayra.Api.Infrastructure.Localization;

public record DateConversionDto(string GregorianDate, string HijriDate, int HijriYear, int HijriMonth, int HijriDay);

public interface IHijriDateService
{
    DateConversionDto FromGregorian(DateOnly date);
}

public class HijriDateService : IHijriDateService
{
    private readonly UmAlQuraCalendar _calendar = new();

    public DateConversionDto FromGregorian(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        var year = _calendar.GetYear(value);
        var month = _calendar.GetMonth(value);
        var day = _calendar.GetDayOfMonth(value);
        return new DateConversionDto(date.ToString("yyyy-MM-dd"), $"{year:0000}-{month:00}-{day:00}", year, month, day);
    }
}
