using MusicReviews.Application.Catalog;
using MusicReviews.Application.Catalog.Dtos;

namespace MusicReviews.UnitTests.Catalog;

/// <summary>
/// Tests del reordenamiento de resultados de busqueda.
/// </summary>
/// <remarks>
/// El caso que motivo esta logica: buscar "The Dark Side of the Moon" devolvia decenas
/// de release-groups con ese titulo exacto —todos con el mismo score— y el de Pink Floyd
/// no entraba en la primera pagina. El orden textual de MusicBrainz no puede distinguir
/// entre homonimos, asi que hace falta una señal de popularidad.
/// </remarks>
public class AlbumSearchRankingTests
{
    private static AlbumSummaryDto Album(
        string mbid,
        int matchScore = 100,
        int releaseCount = 0,
        string? primaryType = "Album",
        int? year = null,
        string title = "The Dark Side of the Moon",
        string artist = "Artista") =>
        new(
            Id: null,
            MusicBrainzId: mbid,
            Title: title,
            CoverArtUrl: null,
            ReleaseDate: year is null ? null : new DateOnly(year.Value, 1, 1),
            PrimaryType: primaryType,
            ArtistName: artist,
            ArtistMusicBrainzId: "a0000000-0000-0000-0000-000000000000",
            MatchScore: matchScore,
            ReleaseCount: releaseCount);

    private static string[] IdsOf(IEnumerable<AlbumSummaryDto> albums) =>
        [.. albums.Select(a => a.MusicBrainzId)];

    // ------------------------------------------------------------------
    // El caso real
    // ------------------------------------------------------------------

    [Fact]
    public void Rank_EntreHomonimosConIgualScore_GanaElQueTieneMasEdiciones()
    {
        // Los tres se llaman igual y matchean igual de bien: es exactamente lo que
        // devuelve MusicBrainz para un disco famoso.
        var albums = new[]
        {
            Album("c0000000-0000-0000-0000-000000000001", releaseCount: 1, primaryType: "EP", year: 2004),
            Album("c0000000-0000-0000-0000-000000000002", releaseCount: 1, year: 2016),
            Album("f0000000-0000-0000-0000-00000000pink", releaseCount: 470, year: 1973)
        };

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal("f0000000-0000-0000-0000-00000000pink", ranked[0].MusicBrainzId);
    }

    [Fact]
    public void Rank_ConUnScoreClaramenteMayor_NoLoDesplazaLaPopularidad()
    {
        // Un match textual mucho peor no deberia ganar por ser popular: si el usuario
        // escribio otra cosa, no importa cuantas ediciones tenga.
        var albums = new[]
        {
            Album("b0000000-0000-0000-0000-000000000001", matchScore: 40, releaseCount: 900),
            Album("a0000000-0000-0000-0000-000000000002", matchScore: 100, releaseCount: 2)
        };

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal("a0000000-0000-0000-0000-000000000002", ranked[0].MusicBrainzId);
    }

    [Fact]
    public void Rank_DentroDelMismoTramoDeScore_MandaLaPopularidad()
    {
        // 100 contra 97 es ruido del indice; 400 ediciones contra 1 no lo es.
        var albums = new[]
        {
            Album("a0000000-0000-0000-0000-000000000001", matchScore: 100, releaseCount: 1),
            Album("b0000000-0000-0000-0000-000000000002", matchScore: 97, releaseCount: 400)
        };

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal("b0000000-0000-0000-0000-000000000002", ranked[0].MusicBrainzId);
    }

    // ------------------------------------------------------------------
    // Desempates
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Album", "Single")]
    [InlineData("Album", "EP")]
    [InlineData("EP", "Single")]
    [InlineData("Album", "Broadcast")]
    public void Rank_ConTodoLoDemasIgual_PrefiereElTipoMasRelevante(string mejor, string peor)
    {
        var albums = new[]
        {
            Album("b0000000-0000-0000-0000-000000000002", primaryType: peor),
            Album("a0000000-0000-0000-0000-000000000001", primaryType: mejor)
        };

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal(mejor, ranked[0].PrimaryType);
    }

    [Fact]
    public void Rank_LosQueNoTienenTipo_VanDespuesDeLosQueSi()
    {
        var albums = new[]
        {
            Album("a0000000-0000-0000-0000-000000000001", primaryType: null),
            Album("b0000000-0000-0000-0000-000000000002", primaryType: "Single")
        };

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal("Single", ranked[0].PrimaryType);
        Assert.Null(ranked[1].PrimaryType);
    }

    [Fact]
    public void Rank_ConTodoLoDemasIgual_PrefiereElLanzamientoOriginal()
    {
        var albums = new[]
        {
            Album("b0000000-0000-0000-0000-000000000002", year: 2016),
            Album("a0000000-0000-0000-0000-000000000001", year: 1973)
        };

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal(1973, ranked[0].ReleaseDate!.Value.Year);
    }

    [Fact]
    public void Rank_LosQueNoTienenFecha_VanAlFinal()
    {
        // Sin fecha no hay forma de saber si es el original o una reedicion suelta.
        var albums = new[]
        {
            Album("a0000000-0000-0000-0000-000000000001", year: null),
            Album("b0000000-0000-0000-0000-000000000002", year: 2020)
        };

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal(2020, ranked[0].ReleaseDate!.Value.Year);
        Assert.Null(ranked[1].ReleaseDate);
    }

    [Fact]
    public void Rank_ConTodosLosCriteriosEmpatados_EsDeterminista()
    {
        // Sin un desempate final, dos llamadas identicas podrian devolver ordenes
        // distintos y el usuario veria la lista saltar entre recargas.
        var albums = new[]
        {
            Album("c0000000-0000-0000-0000-000000000003"),
            Album("a0000000-0000-0000-0000-000000000001"),
            Album("b0000000-0000-0000-0000-000000000002")
        };

        var first = IdsOf(AlbumSearchRanking.Rank(albums));
        var second = IdsOf(AlbumSearchRanking.Rank(albums.Reverse()));

        Assert.Equal(first, second);
        Assert.Equal(
            new[]
            {
                "a0000000-0000-0000-0000-000000000001",
                "b0000000-0000-0000-0000-000000000002",
                "c0000000-0000-0000-0000-000000000003"
            },
            first);
    }

    // ------------------------------------------------------------------
    // Invariantes
    // ------------------------------------------------------------------

    [Fact]
    public void Rank_ConListaVacia_DevuelveVacio()
    {
        Assert.Empty(AlbumSearchRanking.Rank(Array.Empty<AlbumSummaryDto>()));
        Assert.Empty(AlbumSearchRanking.RankArtists(Array.Empty<ArtistSearchItemDto>()));
    }

    [Fact]
    public void Rank_NoPierdeNiDuplicaResultados()
    {
        // Reordenar nunca puede cambiar el conjunto: un resultado que MusicBrainz
        // devolvio y aca desaparece es un disco que el usuario no va a encontrar.
        var albums = Enumerable.Range(1, 50)
            .Select(i => Album(
                $"{i:d8}-0000-0000-0000-000000000000",
                matchScore: 100 - (i % 7) * 5,
                releaseCount: (i * 13) % 40,
                primaryType: i % 3 == 0 ? "Single" : "Album",
                year: i % 5 == 0 ? null : 1970 + i))
            .ToArray();

        var ranked = AlbumSearchRanking.Rank(albums);

        Assert.Equal(albums.Length, ranked.Count);
        Assert.Equal(IdsOf(albums).Order(), IdsOf(ranked).Order());
    }

    // ------------------------------------------------------------------
    // Artistas
    // ------------------------------------------------------------------

    private static ArtistSearchItemDto Artist(string mbid, int score, int? localId = null) =>
        new(localId, mbid, "Nombre", null, null, "Group", score);

    [Fact]
    public void Rank_Artistas_OrdenaPorScoreDescendente()
    {
        var artists = new[]
        {
            Artist("a0000000-0000-0000-0000-000000000001", 55),
            Artist("b0000000-0000-0000-0000-000000000002", 100),
            Artist("c0000000-0000-0000-0000-000000000003", 80)
        };

        var ranked = AlbumSearchRanking.RankArtists(artists);

        Assert.Equal(
            new[] { 100, 80, 55 },
            ranked.Select(a => a.MatchScore).ToArray());
    }

    [Fact]
    public void Rank_Artistas_ConMismoScore_PrefiereElYaCacheado()
    {
        // Un artista que ya esta en la base es uno que alguien de este sitio consulto:
        // mejor apuesta que uno que nadie miro nunca.
        var artists = new[]
        {
            Artist("a0000000-0000-0000-0000-000000000001", 100),
            Artist("b0000000-0000-0000-0000-000000000002", 100, localId: 7)
        };

        var ranked = AlbumSearchRanking.RankArtists(artists);

        Assert.Equal(7, ranked[0].Id);
    }
}
