using System.Globalization;

namespace Opstrax.Api.Services;

/// <summary>
/// Encodes untrusted values for CSV downloads that may be opened by a spreadsheet.
/// RFC 4180 quoting alone does not prevent a leading formula marker from executing.
/// </summary>
public static class SpreadsheetSafeCsv
{
    public static string Cell(object? raw, bool quoteAlways = false)
    {
        var value = raw switch
        {
            null or DBNull => string.Empty,
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => raw.ToString() ?? string.Empty,
        };

        var firstNonWhitespace = value.AsSpan().TrimStart();
        if (!firstNonWhitespace.IsEmpty && firstNonWhitespace[0] is '=' or '+' or '-' or '@')
            value = "'" + value;

        if (quoteAlways || value.IndexOfAny([',', '"', '\n', '\r']) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    public static string Row(IEnumerable<object?> cells, bool quoteAlways = false) =>
        string.Join(",", cells.Select(cell => Cell(cell, quoteAlways)));
}
