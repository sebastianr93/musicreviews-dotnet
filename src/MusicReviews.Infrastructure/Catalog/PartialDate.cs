using System.Globalization;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Parser de las fechas parciales de MusicBrainz.
/// </summary>
/// <remarks>
/// MusicBrainz devuelve "1997", "1997-06" o "1997-06-16" segun lo que se sepa del
/// lanzamiento. Guardar eso en un <c>DateOnly</c> pelado perderia la diferencia entre
/// "salio en 1997" y "salio el 1 de enero de 1997", asi que se normaliza al primer dia
/// del periodo conocido y se conserva la precision aparte.
/// </remarks>
public static class PartialDate
{
    public const int PrecisionUnknown = 0;
    public const int PrecisionYear = 1;
    public const int PrecisionMonth = 2;
    public const int PrecisionDay = 3;

    public static (DateOnly? Date, int Precision) Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, PrecisionUnknown);
        }

        var text = value.Trim();

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var full))
        {
            return (full, PrecisionDay);
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month))
        {
            return (month, PrecisionMonth);
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && year is >= 1 and <= 9999)
        {
            return (new DateOnly(year, 1, 1), PrecisionYear);
        }

        return (null, PrecisionUnknown);
    }
}
