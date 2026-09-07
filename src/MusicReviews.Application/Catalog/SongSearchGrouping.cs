using System.Text;
using MusicReviews.Application.Catalog.Dtos;

namespace MusicReviews.Application.Catalog;

/// <summary>
/// Colapsa las grabaciones de un mismo tema en un unico resultado y las ordena.
/// </summary>
/// <remarks>
/// <para>
/// <b>El problema.</b> MusicBrainz no modela "canciones", modela <i>grabaciones</i>: hay
/// una fila por cada version registrada de un tema —el master original, cada
/// remasterizacion, cada version en vivo, cada edicion por pais, cada recopilatorio—.
/// Buscar "Smells Like Teen Spirit" devuelve la misma cancion cien veces. Mostrar eso
/// tal cual convierte la busqueda de canciones en una lista inutilizable.
/// </para>
/// <para>
/// <b>La clave de agrupacion</b> es (titulo normalizado, artista normalizado). No se
/// puede usar el MBID de la grabacion, que es justamente lo que difiere; ni la duracion,
/// que varia entre ediciones y a veces falta.
/// </para>
/// <para>
/// <b>La normalizacion tiene que ser agresiva</b> porque las diferencias entre versiones
/// viven en el titulo: "Song", "Song (Remastered 2011)", "Song (Live at Reading)",
/// "Song - 2004 Remaster". Se quitan los parentesis y corchetes enteros, y la cola tras
/// un guion solo cuando contiene una palabra de version. Esa asimetria es deliberada:
/// entre parentesis casi siempre hay una anotacion de version, mientras que un guion
/// suele separar partes del titulo real ("Hell Is for Children - Part 2"), asi que
/// cortarlo siempre uniria canciones distintas.
/// </para>
/// <para>
/// <b>El costo asumido.</b> Un titulo cuya unica diferencia real esta entre parentesis
/// —"(I Can't Get No) Satisfaction" frente a "Satisfaction"— se agrupa igual. Se
/// prefiere ese falso positivo ocasional a mostrar cuarenta filas identicas: el usuario
/// llega igual al album correcto, que es a donde lleva el resultado.
/// </para>
/// </remarks>
public static class SongSearchGrouping
{
    /// <inheritdoc cref="AlbumSearchRanking.ScoreTolerance"/>
    public const int ScoreTolerance = 10;

    /// <summary>
    /// Palabras que delatan una anotacion de version en la cola tras un guion.
    /// </summary>
    private static readonly string[] VersionKeywords =
    [
        "remaster", "remastered", "remix", "mix", "live", "version", "edit",
        "mono", "stereo", "demo", "take", "instrumental", "acoustic", "radio",
        "single", "album", "session", "outtake", "reprise", "bonus", "anniversary",
        "deluxe", "remasterizado", "remasterizada", "envivo", "directo"
    ];

    /// <summary>
    /// Agrupa por (titulo, artista) y ordena el resultado por relevancia.
    /// La entrada son grabaciones sueltas, cada una con <c>VersionCount</c> en 1.
    /// </summary>
    public static IReadOnlyList<SongSearchItemDto> GroupAndRank(IEnumerable<SongSearchItemDto> songs)
    {
        var groups = new Dictionary<string, List<SongSearchItemDto>>(StringComparer.Ordinal);

        foreach (var song in songs)
        {
            var key = GroupKey(song.Title, song.ArtistName);

            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = [];
                groups[key] = bucket;
            }

            bucket.Add(song);
        }

        var merged = new List<SongSearchItemDto>(groups.Count);

        foreach (var bucket in groups.Values)
        {
            merged.Add(Merge(bucket));
        }

        return Rank(merged);
    }

    /// <summary>
    /// Funde un grupo en una sola fila. El representante es la grabacion con mejor
    /// score y, a igualdad, la que ya trae album: sin album el resultado no lleva a
    /// ningun lado y el usuario no puede hacer nada con el.
    /// </summary>
    private static SongSearchItemDto Merge(List<SongSearchItemDto> bucket)
    {
        var best = bucket
            .OrderByDescending(song => song.MatchScore)
            .ThenByDescending(song => song.AlbumMusicBrainzId is not null)
            .ThenBy(song => song.ReleaseDate ?? DateOnly.MaxValue)
            .ThenBy(song => song.MusicBrainzId, StringComparer.Ordinal)
            .First();

        // Los datos que le falten al representante se completan desde el resto del
        // grupo: la version que mejor matchea no siempre es la que trae la fecha
        // original o la portada, y todas describen la misma cancion.
        var withAlbum = bucket.FirstOrDefault(song => song.AlbumMusicBrainzId is not null);
        var earliest = bucket
            .Where(song => song.ReleaseDate is not null)
            .OrderBy(song => song.ReleaseDate)
            .FirstOrDefault();

        return best with
        {
            AlbumTitle = best.AlbumTitle ?? withAlbum?.AlbumTitle,
            AlbumMusicBrainzId = best.AlbumMusicBrainzId ?? withAlbum?.AlbumMusicBrainzId,
            CoverArtUrl = best.CoverArtUrl
                ?? bucket.FirstOrDefault(song => song.CoverArtUrl is not null)?.CoverArtUrl,
            LengthMilliseconds = best.LengthMilliseconds
                ?? bucket.FirstOrDefault(song => song.LengthMilliseconds is not null)?.LengthMilliseconds,
            // La fecha del grupo es la mas vieja: es cuando salio la cancion, no cuando
            // salio la reedicion que quedo de representante.
            ReleaseDate = earliest?.ReleaseDate,
            VersionCount = bucket.Sum(song => song.VersionCount)
        };
    }

    /// <summary>
    /// Mismo criterio que <see cref="AlbumSearchRanking.Rank"/>: dentro de la tolerancia
    /// de score el orden lo decide la popularidad, que aca es la cantidad de versiones.
    /// </summary>
    private static IReadOnlyList<SongSearchItemDto> Rank(List<SongSearchItemDto> songs)
    {
        if (songs.Count == 0)
        {
            return [];
        }

        var threshold = songs.Max(song => song.MatchScore) - ScoreTolerance;

        return
        [
            .. songs
                .OrderByDescending(song => song.MatchScore >= threshold)
                .ThenByDescending(song => song.MatchScore >= threshold ? 0 : song.MatchScore)
                .ThenByDescending(song => song.VersionCount)
                // Un resultado sin album no lleva a ninguna parte: va despues de los
                // que si sirven, aunque empate en todo lo demas.
                .ThenByDescending(song => song.AlbumMusicBrainzId is not null)
                .ThenBy(song => song.ReleaseDate ?? DateOnly.MaxValue)
                .ThenBy(song => song.MusicBrainzId, StringComparer.Ordinal)
        ];
    }

    /// <summary>Clave de agrupacion. Publica para poder testearla sola.</summary>
    /// <remarks>
    /// El separador no es decorativo: sin el, ("abc", "de") y ("ab", "cde") producen la
    /// misma clave. La barra es segura porque <see cref="Normalize"/> deja unicamente
    /// letras, digitos y espacios, asi que no puede aparecer en ninguna de las partes.
    /// </remarks>
    public static string GroupKey(string title, string artistName) =>
        $"{NormalizeTitle(title)}|{Normalize(artistName)}";

    /// <summary>
    /// Normaliza un titulo quitando las anotaciones de version.
    /// </summary>
    public static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var withoutBrackets = RemoveBracketed(title);
        var withoutTail = RemoveVersionTail(withoutBrackets);
        var normalized = Normalize(withoutTail);

        // Si sacar los parentesis dejo el titulo vacio —el caso de un tema que se
        // llama literalmente "(Untitled)"— vale mas el titulo entero que una clave
        // vacia en la que caerian todos.
        return normalized.Length == 0 ? Normalize(title) : normalized;
    }

    /// <summary>
    /// Minusculas, sin acentos, sin puntuacion y con los espacios colapsados.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sacar los acentos importa mas de lo que parece: el mismo tema aparece cargado
    /// como "Cancion" y como "Canción" segun quien lo haya subido, y son dos claves
    /// distintas para cualquier comparacion literal.
    /// </para>
    /// <para>
    /// La forma canonica de hacerlo seria <c>Normalize(NormalizationForm.FormD)</c> y
    /// descartar las marcas diacriticas. <b>No sirve aca:</b> el proyecto corre con
    /// <c>InvariantGlobalization=true</c>, y en ese modo la normalizacion Unicode no
    /// hace nada —no lanza, devuelve la cadena tal cual—, asi que la tilde sobrevive y
    /// el fallo es silencioso. Por eso el plegado es una tabla explicita.
    /// </para>
    /// <para>
    /// La tabla cubre latin basico y extendido, que es donde estan los acentos que
    /// generan duplicados en la practica. Lo que no esta en la tabla pero <b>si es
    /// letra o digito se conserva tal cual</b>: un titulo en cirilico o en japones tiene
    /// que seguir siendo distinguible, y mapearlo a nada agruparia todos los titulos de
    /// esos alfabetos en una unica clave vacia.
    /// </para>
    /// </remarks>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        var lastWasSpace = true;

        foreach (var c in lowered)
        {
            var folded = Fold(c);

            if (folded is not null)
            {
                builder.Append(folded);
                lastWasSpace = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Equivalente ASCII de una letra acentuada, o null si no hay que plegarla.
    /// </summary>
    /// <remarks>
    /// Las ligaduras devuelven dos caracteres porque es como se escriben cuando no se
    /// dispone del caracter compuesto: "æ" se carga tambien como "ae" y "ß" como "ss",
    /// y las dos formas tienen que caer en la misma clave.
    /// </remarks>
    private static string? Fold(char c) => c switch
    {
        '\u00e0' or '\u00e1' or '\u00e2' or '\u00e3' or '\u00e4' or '\u00e5' or '\u0101' or '\u0103' or '\u0105' => "a",
        '\u00e6' => "ae",
        '\u00e7' or '\u0107' or '\u0109' or '\u010b' or '\u010d' => "c",
        '\u010f' or '\u0111' or '\u00f0' => "d",
        '\u00e8' or '\u00e9' or '\u00ea' or '\u00eb' or '\u0113' or '\u0115' or '\u0117' or '\u0119' or '\u011b' => "e",
        '\u011d' or '\u011f' or '\u0121' or '\u0123' => "g",
        '\u0125' or '\u0127' => "h",
        '\u00ec' or '\u00ed' or '\u00ee' or '\u00ef' or '\u0129' or '\u012b' or '\u012d' or '\u012f' or '\u0131' => "i",
        '\u0135' => "j",
        '\u0137' => "k",
        '\u013a' or '\u013c' or '\u013e' or '\u0140' or '\u0142' => "l",
        '\u00f1' or '\u0144' or '\u0146' or '\u0148' => "n",
        '\u00f2' or '\u00f3' or '\u00f4' or '\u00f5' or '\u00f6' or '\u00f8' or '\u014d' or '\u014f' or '\u0151' => "o",
        '\u0153' => "oe",
        '\u0155' or '\u0157' or '\u0159' => "r",
        '\u015b' or '\u015d' or '\u015f' or '\u0161' => "s",
        '\u00df' => "ss",
        '\u0163' or '\u0165' or '\u0167' => "t",
        '\u00fe' => "th",
        '\u00f9' or '\u00fa' or '\u00fb' or '\u00fc' or '\u0169' or '\u016b' or '\u016d' or '\u016f' or '\u0171' or '\u0173' => "u",
        '\u0175' => "w",
        '\u00fd' or '\u00ff' or '\u0177' => "y",
        '\u017a' or '\u017c' or '\u017e' => "z",
        _ => null
    };

    /// <summary>
    /// Quita todo lo que este entre parentesis o corchetes, incluidos los anidados.
    /// </summary>
    private static string RemoveBracketed(string title)
    {
        var builder = new StringBuilder(title.Length);
        var depth = 0;

        foreach (var c in title)
        {
            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                // Un cierre sin apertura es un titulo mal cargado; se ignora en vez
                // de dejar la profundidad en negativo y descartar el resto.
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Corta la cola tras el ultimo guion, pero solo si parece una anotacion de version.
    /// </summary>
    private static string RemoveVersionTail(string title)
    {
        var separator = title.LastIndexOf(" - ", StringComparison.Ordinal);

        if (separator <= 0)
        {
            return title;
        }

        var tail = Normalize(title[(separator + 3)..]);

        if (tail.Length == 0)
        {
            return title[..separator];
        }

        var words = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Any(word => VersionKeywords.Contains(word, StringComparer.Ordinal))
            ? title[..separator]
            : title;
    }
}
