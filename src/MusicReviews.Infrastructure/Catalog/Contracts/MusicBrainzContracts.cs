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

internal sealed record MbArtistCredit
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("artist")]
    public MbArtist? Artist { get; init; }
}
