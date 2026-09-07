using MusicReviews.Application.News;

namespace MusicReviews.UnitTests.News;

/// <summary>
/// El parser de feeds. Es el unico punto de la aplicacion que procesa contenido de un
/// tercero sobre el que no se tiene ningun control, asi que la mitad de estos tests no
/// verifican que lea bien un feed correcto sino que no se rompa —ni deje pasar nada
/// peligroso— con uno que no lo es.
/// </summary>
public class RssParserTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/"
             xmlns:content="http://purl.org/rss/1.0/modules/content/">
          <channel>
            <title>Ejemplo</title>
            <item>
              <title>Una banda anuncia disco &amp; gira</title>
              <link>https://ejemplo.test/nota-1</link>
              <pubDate>Tue, 03 Jun 2025 09:00:00 +0000</pubDate>
              <description>&lt;p&gt;El grupo &lt;b&gt;confirmo&lt;/b&gt; el album.&lt;/p&gt;</description>
              <enclosure url="https://ejemplo.test/tapa.jpg" type="image/jpeg" length="1000"/>
            </item>
            <item>
              <title>Segunda nota</title>
              <link>https://ejemplo.test/nota-2</link>
              <pubDate>Mon, 02 Jun 2025 09:00:00 GMT</pubDate>
              <description>Texto sin html.</description>
              <media:thumbnail url="https://ejemplo.test/thumb.png"/>
            </item>
            <item>
              <title>Sin enlace</title>
              <description>Se descarta.</description>
            </item>
            <item>
              <link>https://ejemplo.test/sin-titulo</link>
              <description>Tampoco entra.</description>
            </item>
            <item>
              <title>Enlace peligroso</title>
              <link>javascript:alert(1)</link>
              <description>No tiene que pasar.</description>
            </item>
            <item>
              <title>Imagen en el html</title>
              <link>https://ejemplo.test/nota-3</link>
              <content:encoded>&lt;div&gt;&lt;img src="https://ejemplo.test/inline.jpg" /&gt;Cuerpo&lt;/div&gt;</content:encoded>
            </item>
          </channel>
        </rss>
        """;

    private const string Atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Atom de ejemplo</title>
          <entry>
            <title>Nota en Atom</title>
            <link rel="self" href="https://atom.test/feed"/>
            <link rel="alternate" href="https://atom.test/nota"/>
            <published>2025-06-01T12:00:00Z</published>
            <summary>Resumen corto.</summary>
          </entry>
        </feed>
        """;

    // ------------------------------------------------------------------
    // RSS
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_DescartaLosItemsQueNoSePuedenPublicar()
    {
        // Sin titulo no hay que mostrar; sin enlace la noticia no lleva a la fuente, y
        // publicar un extracto que no se puede atribuir es justo lo que no se quiere.
        var items = RssParser.Parse(Rss, "Ejemplo");

        Assert.Equal(3, items.Count);
        Assert.DoesNotContain(items, item => item.Title == "Sin enlace");
        Assert.DoesNotContain(items, item => item.Title == "Enlace peligroso");
    }

    [Fact]
    public void Parse_LeeElPrimerItemCompleto()
    {
        var item = RssParser.Parse(Rss, "Ejemplo")[0];

        Assert.Equal("Una banda anuncia disco & gira", item.Title);
        Assert.Equal("El grupo confirmo el album.", item.Excerpt);
        Assert.Equal("https://ejemplo.test/nota-1", item.Url);
        Assert.Equal("https://ejemplo.test/tapa.jpg", item.ImageUrl);
        Assert.Equal(new DateTimeOffset(2025, 6, 3, 9, 0, 0, TimeSpan.Zero), item.PublishedAt);

        // La atribucion viaja siempre: el lector tiene que saber de donde salio.
        Assert.Equal("Ejemplo", item.SourceName);
    }

    [Fact]
    public void Parse_TomaLaImagenDeMediaThumbnailCuandoNoHayEnclosure()
    {
        var item = RssParser.Parse(Rss, "Ejemplo")[1];

        Assert.Equal("https://ejemplo.test/thumb.png", item.ImageUrl);
        Assert.Equal(new DateTimeOffset(2025, 6, 2, 9, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Fact]
    public void Parse_ComoUltimoRecurso_RescataLaImagenDelHtmlDelResumen()
    {
        var item = RssParser.Parse(Rss, "Ejemplo")[2];

        Assert.Equal("https://ejemplo.test/inline.jpg", item.ImageUrl);
        Assert.Equal("Cuerpo", item.Excerpt);

        // Un item sin fecha se publica igual; queda al final del orden.
        Assert.Null(item.PublishedAt);
    }

    // ------------------------------------------------------------------
    // Atom
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_LeeAtomYPrefiereElEnlaceAlternate()
    {
        // El link rel="self" apunta al feed, no al articulo: seguirlo dejaria al lector
        // mirando XML.
        var item = Assert.Single(RssParser.Parse(Atom, "Atom"));

        Assert.Equal("Nota en Atom", item.Title);
        Assert.Equal("https://atom.test/nota", item.Url);
        Assert.Equal("Resumen corto.", item.Excerpt);
        Assert.Equal(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    // ------------------------------------------------------------------
    // Robustez
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<rss><channel><item>")]
    [InlineData("<html><body>no soy un feed</body></html>")]
    [InlineData("{\"esto\":\"es json\"}")]
    public void Parse_ConEntradaQueNoEsUnFeed_DevuelveVacioSinLanzar(string? xml)
    {
        // La seccion de noticias es relleno del feed: un medio que devuelve una pagina
        // de error con 200 no puede tumbar la portada.
        Assert.Empty(RssParser.Parse(xml, "X"));
    }

    // ------------------------------------------------------------------
    // Texto
    // ------------------------------------------------------------------

    [Fact]
    public void Excerpt_RecortaSinPartirLaPalabra()
    {
        var frase = string.Join(" ", Enumerable.Repeat("palabra", 60));
        var excerpt = RssParser.Excerpt(frase)!;

        Assert.True(excerpt.Length <= RssParser.ExcerptMaxLength + 3);
        Assert.EndsWith("...", excerpt);
        Assert.EndsWith("palabra", excerpt[..^3].TrimEnd());
    }

    [Fact]
    public void Excerpt_ConUnTokenEnorme_CortaSecoEnVezDeDevolverCasiNada()
    {
        // Cortar en el ultimo espacio dejaria un extracto de 50 caracteres sobre un
        // limite de 200. Ahi conviene partir la palabra.
        var texto = new string('a', 50) + " " + new string('b', 300);

        var excerpt = RssParser.Excerpt(texto)!;

        Assert.True(excerpt.Length > RssParser.ExcerptMaxLength / 2);
        Assert.True(excerpt.Length <= RssParser.ExcerptMaxLength + 3);
    }

    [Fact]
    public void StripHtml_DecodificaDespuesDeSacarLasEtiquetas()
    {
        // Al reves, "&lt;b&gt;" se convertiria en "<b>" y el paso siguiente lo borraria
        // como si fuera una etiqueta, perdiendo texto que el autor escribio a proposito.
        Assert.Contains("<b>texto</b>", RssParser.StripHtml("&lt;b&gt;texto&lt;/b&gt;"));
    }

    [Fact]
    public void Excerpt_ConEtiquetasPegadas_NoJuntaLasPalabras() =>
        Assert.Equal("uno dos", RssParser.Excerpt("<b>uno</b><b>dos</b>"));

    [Fact]
    public void Excerpt_ConSoloHtml_DevuelveNull() =>
        Assert.Null(RssParser.Excerpt("<p></p>"));

    // ------------------------------------------------------------------
    // Urls
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("http://a.test/x")]
    [InlineData("https://a.test/x")]
    public void SafeUrl_AceptaHttpYHttps(string url) =>
        Assert.NotNull(RssParser.SafeUrl(url));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAA")]
    [InlineData("/nota")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SafeUrl_RechazaTodoLoDemas(string? url)
    {
        // El enlace termina en un <a href> del navegador y el contenido viene de un
        // tercero: un "javascript:" ahi es ejecucion de codigo en la sesion del usuario.
        Assert.Null(RssParser.SafeUrl(url));
    }
}
