using System.Text.Json.Serialization;

namespace MusicReviews.Infrastructure.Catalog.Contracts;

// Contratos de la API de MusicBrainz (ws/2, fmt=json).
// Las claves vienen en kebab-case ("sort-name", "first-release-date"), que ninguna
// politica de nombres de System.Text.Json cubre, asi que van anotadas explicitamente.
// Son internos a Infrastructure: la forma de la API externa no se filtra hacia arriba.

internal sealed record MbArtistSearchResponse
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("artists")]
    public IReadOnlyList<MbArtist> Artists { get; init; } = [];
}

internal sealed record MbArtist
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("sort-name")]
    public string? SortName { get; init; }

    [JsonPropertyName("disambiguation")]
    public string? Disambiguation { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Relevancia 0-100. Solo viene en /search.</summary>
    [JsonPropertyName("score")]
    public int Score { get; init; }

    /// <summary>
    /// Enlaces a otros sitios. Solo llegan pidiendo <c>inc=url-rels</c> en el lookup, y
    /// son el unico camino hacia una foto: MusicBrainz no aloja imagenes.
    /// </summary>
    [JsonPropertyName("relations")]
    public IReadOnlyList<MbRelation> Relations { get; init; } = [];
}

internal sealed record MbRelation
{
    /// <summary>Tipo de enlace: "wikidata", "official homepage", "discogs", etc.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("url")]
    public MbRelationUrl? Url { get; init; }
}

internal sealed record MbRelationUrl
{
    [JsonPropertyName("resource")]
    public string? Resource { get; init; }
}

internal sealed record MbReleaseGroupSearchResponse
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("release-groups")]
    public IReadOnlyList<MbReleaseGroup> ReleaseGroups { get; init; } = [];
}

/// <summary>
/// Respuesta del endpoint de browse (por artista). Difiere del search: el total
/// viene en "release-group-count", no en "count".
/// </summary>
internal sealed record MbReleaseGroupBrowseResponse
{
    [JsonPropertyName("release-group-count")]
    public int Count { get; init; }

    [JsonPropertyName("release-group-offset")]
    public int Offset { get; init; }

    [JsonPropertyName("release-groups")]
    public IReadOnlyList<MbReleaseGroup> ReleaseGroups { get; init; } = [];
}

internal sealed record MbReleaseGroup
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; init; } = null!;

    /// <summary>
    /// Relevancia 0-100 que asigna el indice de busqueda. Solo viene en /search,
    /// no en /browse ni en el lookup por MBID.
    /// </summary>
    [JsonPropertyName("score")]
    public int Score { get; init; }

    /// <summary>
    /// Cantidad de ediciones (releases) del release-group. Es la unica señal de
    /// popularidad que expone la API: un disco reeditado cientos de veces es el que
    /// la gente busca; uno con una sola edicion casi nunca lo es.
    /// El campo no esta documentado como garantizado, asi que puede venir en null.
    /// </summary>
    [JsonPropertyName("count")]
    public int? ReleaseCount { get; init; }

    /// <summary>Respaldo de <see cref="ReleaseCount"/> cuando la API no manda "count".</summary>
    [JsonPropertyName("releases")]
    public IReadOnlyList<MbRelease> Releases { get; init; } = [];

    /// <summary>Fecha parcial: puede ser "1997", "1997-06" o "1997-06-16".</summary>
    [JsonPropertyName("first-release-date")]
    public string? FirstReleaseDate { get; init; }

    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; init; }

    [JsonPropertyName("secondary-types")]
    public IReadOnlyList<string> SecondaryTypes { get; init; } = [];

    [JsonPropertyName("artist-credit")]
    public IReadOnlyList<MbArtistCredit> ArtistCredit { get; init; } = [];
}

internal sealed record MbRelease
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Fecha parcial de ESTA edicion, no de la grabacion.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>
    /// Presente en la busqueda de grabaciones: es lo que permite llevar una cancion
    /// hasta el album al que pertenece sin una consulta extra.
    /// </summary>
    [JsonPropertyName("release-group")]
    public MbReleaseGroupRef? ReleaseGroup { get; init; }
}

/// <summary>
/// Version reducida del release-group tal como viene anidada dentro de un release.
/// No se reutiliza <see cref="MbReleaseGroup"/> porque aca no llegan ni el score ni
/// el credito de artista, y un tipo que promete campos que nunca vienen invita a
/// leerlos.
/// </summary>
internal sealed record MbReleaseGroupRef
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; init; }

    [JsonPropertyName("secondary-types")]
    public IReadOnlyList<string> SecondaryTypes { get; init; } = [];
}

internal sealed record MbRecordingSearchResponse
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("recordings")]
    public IReadOnlyList<MbRecording> Recordings { get; init; } = [];
}

/// <summary>
/// Una grabacion. Ojo con el modelo: MusicBrainz tiene una grabacion por cada
/// version registrada de una cancion —el master, cada remasterizacion, cada version
/// en vivo, cada edicion por pais—, asi que buscar un tema conocido devuelve decenas
/// de filas que para el usuario son la misma cancion. Agruparlas es trabajo nuestro
/// (ver <c>SongSearchGrouping</c>).
/// </summary>
internal sealed record MbRecording
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; init; } = null!;

    /// <summary>Relevancia 0-100. Solo viene en /search.</summary>
    [JsonPropertyName("score")]
    public int Score { get; init; }

    /// <summary>Duracion en milisegundos. Puede faltar.</summary>
    [JsonPropertyName("length")]
    public int? Length { get; init; }

    [JsonPropertyName("first-release-date")]
    public string? FirstReleaseDate { get; init; }

    [JsonPropertyName("artist-credit")]
    public IReadOnlyList<MbArtistCredit> ArtistCredit { get; init; } = [];

    /// <summary>Ediciones en las que aparece esta grabacion, con su release-group.</summary>
    [JsonPropertyName("releases")]
    public IReadOnlyList<MbRelease> Releases { get; init; } = [];
}

internal sealed record MbArtistCredit
{
    /// <summary>Nombre con el que se acredito al artista en ESTE lanzamiento.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Texto que une este credito con el siguiente: " feat. ", " &amp; ", ", ".</summary>
    [JsonPropertyName("joinphrase")]
    public string? JoinPhrase { get; init; }

    [JsonPropertyName("artist")]
    public MbArtist? Artist { get; init; }
}

internal static class MbArtistCreditExtensions
{
    /// <summary>
    /// Reconstruye el credito completo respetando las joinphrase.
    /// </summary>
    /// <remarks>
    /// Tomar solo el primer credito muestra "Jay-Z" donde corresponde
    /// "Jay-Z &amp; Linkin Park", y en un split o un colaborativo puede atribuir el
    /// album entero al artista equivocado. MusicBrainz modela el credito como una
    /// lista ordenada justamente porque el nombre correcto es la concatenacion.
    /// </remarks>
    public static string FormatCredit(this IReadOnlyList<MbArtistCredit> credits)
    {
        if (credits.Count == 0)
        {
            return string.Empty;
        }

        return string.Concat(credits.Select(credit =>
            (credit.Name ?? credit.Artist?.Name ?? string.Empty) + (credit.JoinPhrase ?? string.Empty)))
            .Trim();
    }
}
