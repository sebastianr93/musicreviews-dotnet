using MusicReviews.Application.Catalog;
using MusicReviews.Application.Catalog.Dtos;

namespace MusicReviews.UnitTests.Catalog;

/// <summary>
/// El agrupado de grabaciones. Es logica pura sobre listas, asi que se prueba sin base
/// y sin red; lo unico que importa aca es que la misma cancion colapse en una fila y
/// que dos canciones distintas nunca lo hagan.
/// </summary>
public class SongSearchGroupingTests
{
    private static SongSearchItemDto Song(
        string id,
        string title,
        string artist,
        string? albumId = null,
        string? albumTitle = null,
        string? date = null,
        int score = 100,
        int? lengthMs = null,
        string? coverUrl = null) =>
        new(id, title, artist, "artist-mbid", lengthMs, albumTitle, albumId, coverUrl,
            date is null ? null : DateOnly.Parse(date), VersionCount: 1, MatchScore: score);

    // ------------------------------------------------------------------
    // Normalizacion
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("  Hello   World  ", "hello world")]
    [InlineData("Don't Stop Me, Now!", "don t stop me now")]
    [InlineData("   ", "")]
    [InlineData("!!!", "")]
    public void Normalize_LimpiaEspaciosYPuntuacion(string input, string expected) =>
        Assert.Equal(expected, SongSearchGrouping.Normalize(input));

    [Fact]
    public void Normalize_QuitaLosAcentos()
    {
        // El mismo tema aparece cargado como "Cancion" y como "Cancion" con tilde segun
        // quien lo haya subido: para cualquier comparacion literal son dos claves.
        //
        // Este test es el que sostiene la tabla de plegado escrita a mano. La forma
        // canonica —Normalize(FormD) descartando marcas diacriticas— NO funciona en
        // este proyecto: corre con InvariantGlobalization=true, y en ese modo la
        // normalizacion Unicode no hace nada y devuelve la cadena tal cual. El fallo es
        // silencioso, asi que sin esta comprobacion pasaria inadvertido.
        Assert.Equal(
            SongSearchGrouping.Normalize("Cancion"),
            SongSearchGrouping.Normalize("Canción"));

        Assert.Equal("sigur ros", SongSearchGrouping.Normalize("Sigur Rós"));
    }

    [Theory]
    [InlineData("Encyclopædia", "Encyclopaedia")]
    [InlineData("Straße", "Strasse")]
    public void Normalize_UneLasLigadurasConSuFormaEscrita(string ligature, string spelled)
    {
        // Las dos formas conviven en el catalogo segun quien haya cargado el titulo.
        Assert.Equal(
            SongSearchGrouping.Normalize(spelled),
            SongSearchGrouping.Normalize(ligature));
    }

    [Fact]
    public void Normalize_ConAlfabetosNoLatinos_ConservaElTexto()
    {
        // Lo que la tabla no cubre pero es letra se conserva. Mapearlo a nada agruparia
        // todos los titulos en cirilico o japones en una unica clave vacia.
        var uno = SongSearchGrouping.Normalize("残酷な天使");
        var otro = SongSearchGrouping.Normalize("君の名は");

        Assert.NotEmpty(uno);
        Assert.NotEmpty(otro);
        Assert.NotEqual(uno, otro);
    }

    [Theory]
    [InlineData("Song (Remastered 2011)", "song")]
    [InlineData("Song [Live at Reading]", "song")]
    [InlineData("Song (Live (1994) Version)", "song")]
    [InlineData("Song - 2004 Remaster", "song")]
    public void NormalizeTitle_QuitaLasAnotacionesDeVersion(string input, string expected) =>
        Assert.Equal(expected, SongSearchGrouping.NormalizeTitle(input));

    [Fact]
    public void NormalizeTitle_NoCortaUnaColaQueEsParteDelTitulo()
    {
        // Cortar siempre despues del guion uniria canciones distintas. Solo se corta
        // cuando la cola contiene una palabra de version.
        Assert.Equal(
            "hell is for children part 2",
            SongSearchGrouping.NormalizeTitle("Hell Is for Children - Part 2"));
    }

    [Fact]
    public void NormalizeTitle_ConTituloQueEsSoloUnParentesis_ConservaElOriginal()
    {
        // Sin este resguardo, todos los temas asi caerian en la misma clave vacia.
        Assert.Equal("untitled", SongSearchGrouping.NormalizeTitle("(Untitled)"));
    }

    [Fact]
    public void NormalizeTitle_ConUnCierreSinApertura_NoDescartaElResto() =>
        Assert.Equal("song title", SongSearchGrouping.NormalizeTitle("Song) title"));

    [Fact]
    public void GroupKey_ElSeparadorEvitaLaColisionEntrePartes()
    {
        // Sin separador, ("abc","de") y ("ab","cde") producen la misma clave.
        Assert.NotEqual(
            SongSearchGrouping.GroupKey("abc", "de"),
            SongSearchGrouping.GroupKey("ab", "cde"));
    }

    [Fact]
    public void GroupKey_MismoTemaDistintoFormato_MismaClave() =>
        Assert.Equal(
            SongSearchGrouping.GroupKey("song", "the band"),
            SongSearchGrouping.GroupKey("Song (Live)", "The Band"));

    [Fact]
    public void GroupKey_DistintoArtista_ClaveDistinta() =>
        Assert.NotEqual(
            SongSearchGrouping.GroupKey("Song", "A"),
            SongSearchGrouping.GroupKey("Song", "B"));

    // ------------------------------------------------------------------
    // Agrupado
    // ------------------------------------------------------------------

    [Fact]
    public void GroupAndRank_ColapsaLasVersionesDeLaMismaCancion()
    {
        var result = SongSearchGrouping.GroupAndRank([
            Song("r1", "Smells Like Teen Spirit", "Nirvana", "rg1", "Nevermind", "1991-09-24"),
            Song("r2", "Smells Like Teen Spirit (Remastered)", "Nirvana", "rg1", "Nevermind", "2011-01-01"),
            Song("r3", "Smells Like Teen Spirit (Live)", "Nirvana", "rg2", "Unplugged", "1994-01-01"),
            Song("r4", "Come as You Are", "Nirvana", "rg1", "Nevermind", "1991-09-24")
        ]);

        Assert.Equal(2, result.Count);

        var spirit = result.Single(song => song.Title.StartsWith("Smells"));

        Assert.Equal(3, spirit.VersionCount);

        // La fecha del grupo es la mas vieja: es cuando salio la cancion, no cuando
        // salio la reedicion que quedo de representante.
        Assert.Equal(new DateOnly(1991, 9, 24), spirit.ReleaseDate);
    }

    [Fact]
    public void GroupAndRank_CompletaLosDatosQueLeFaltanAlRepresentante()
    {
        // La version que mejor matchea no siempre es la que trae el album, la portada
        // o la duracion, y todas describen la misma cancion.
        var result = SongSearchGrouping.GroupAndRank([
            Song("r1", "Song", "Band", score: 100),
            Song("r2", "Song (Live)", "Band", "rg9", "El Album", "1990-01-01",
                score: 90, lengthMs: 240_000, coverUrl: "https://example.test/c.jpg")
        ]);

        var song = Assert.Single(result);

        Assert.Equal("r1", song.MusicBrainzId);
        Assert.Equal(100, song.MatchScore);
        Assert.Equal("rg9", song.AlbumMusicBrainzId);
        Assert.Equal("El Album", song.AlbumTitle);
        Assert.Equal("https://example.test/c.jpg", song.CoverArtUrl);
        Assert.Equal(240_000, song.LengthMilliseconds);
        Assert.Equal(2, song.VersionCount);
    }

    // ------------------------------------------------------------------
    // Orden
    // ------------------------------------------------------------------

    [Fact]
    public void GroupAndRank_DentroDeLaToleranciaDeScore_MandaLaCantidadDeVersiones()
    {
        var result = SongSearchGrouping.GroupAndRank([
            Song("a", "Tema Uno", "Banda A", "rg1", "X", "1980-01-01", score: 100),
            Song("b", "Tema Dos", "Banda B", "rg2", "Y", "1980-01-01", score: 95),
            Song("c", "Tema Dos (Live)", "Banda B", "rg2", "Y", "1985-01-01", score: 95),
            Song("d", "Tema Tres", "Banda C", "rg3", "Z", "1980-01-01", score: 40)
        ]);

        Assert.StartsWith("Tema Dos", result[0].Title);

        // Fuera de la tolerancia el score si decide: 40 contra 100 no es ruido del
        // indice, es que el resultado no tiene nada que ver con lo que se busco.
        Assert.Equal("Tema Tres", result[^1].Title);
    }

    [Fact]
    public void GroupAndRank_LoQueNoLlevaANingunAlbum_VaAlFinal()
    {
        // En esta aplicacion se reseña el album: una cancion sin album es un resultado
        // con el que el usuario no puede hacer nada.
        var result = SongSearchGrouping.GroupAndRank([
            Song("x", "Igual", "Sin album", score: 100),
            Song("y", "Igual", "Con album", "rg1", "Album", score: 100)
        ]);

        Assert.Equal("Con album", result[0].ArtistName);
        Assert.Equal("Sin album", result[1].ArtistName);
    }

    [Fact]
    public void GroupAndRank_ElOrdenNoDependeDelOrdenDeEntrada()
    {
        SongSearchItemDto[] entrada =
        [
            Song("b", "Tema B", "A", "rg", "X", "1990-01-01"),
            Song("a", "Tema A", "A", "rg", "X", "1990-01-01")
        ];

        var directo = SongSearchGrouping.GroupAndRank(entrada);
        var invertido = SongSearchGrouping.GroupAndRank(entrada.Reverse());

        Assert.Equal(
            directo.Select(song => song.MusicBrainzId),
            invertido.Select(song => song.MusicBrainzId));
    }

    // ------------------------------------------------------------------
    // Bordes
    // ------------------------------------------------------------------

    [Fact]
    public void GroupAndRank_SinResultados_DevuelveVacio() =>
        Assert.Empty(SongSearchGrouping.GroupAndRank([]));

    [Fact]
    public void GroupAndRank_ConUnaSolaCancion_LaDevuelveIntacta()
    {
        var song = Assert.Single(SongSearchGrouping.GroupAndRank([Song("solo", "Uno", "A")]));

        Assert.Equal("solo", song.MusicBrainzId);
        Assert.Equal(1, song.VersionCount);
    }

    [Fact]
    public void GroupAndRank_ConCancionesSinFecha_NoRompe()
    {
        var result = SongSearchGrouping.GroupAndRank([
            Song("n1", "Sin fecha", "A", "rg1", "X"),
            Song("n2", "Con fecha", "B", "rg2", "Y", "1990-01-01")
        ]);

        Assert.Equal(2, result.Count);
        Assert.Null(result.Single(song => song.Title == "Sin fecha").ReleaseDate);
    }
}
