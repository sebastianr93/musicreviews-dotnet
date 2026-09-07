using System.Net;
using System.Text;
using System.Xml.Linq;
using MusicReviews.Application.News.Dtos;

namespace MusicReviews.Application.News;

/// <summary>
/// Lee un feed RSS 2.0 o Atom y devuelve las noticias ya recortadas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Los dos formatos en un solo parser.</b> Los medios de musica publican en RSS o en
/// Atom segun el CMS que usen, y no hay forma de elegir. Las diferencias son pocas
/// —<c>item</c> contra <c>entry</c>, <c>link</c> como texto contra <c>link</c> como
/// atributo <c>href</c>, <c>pubDate</c> contra <c>published</c>— asi que se normalizan
/// aca y hacia afuera todo es un <see cref="NewsItemDto"/>.
/// </para>
/// <para>
/// <b>Nada de esto puede lanzar por culpa de un feed.</b> Un XML mal formado, un item sin
/// titulo o una fecha en un formato raro son cosas que pasan y no son un error de esta
/// aplicacion: la seccion de noticias es relleno del feed, no puede tumbar la portada.
/// Lo que no se entiende se descarta y ya.
/// </para>
/// </remarks>
public static class RssParser
{
    /// <summary>
    /// Cuanto texto del articulo ajeno se muestra. Corto a proposito: es un extracto
    /// para decidir si entrar, no un sustituto de leer la nota.
    /// </summary>
    public const int ExcerptMaxLength = 200;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    /// <summary>
    /// Devuelve las noticias del feed. Lista vacia si el documento no se puede leer.
    /// </summary>
    public static IReadOnlyList<NewsItemDto> Parse(string? xml, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        if (document.Root is null)
        {
            return [];
        }

        // RSS anida los items dentro de <channel>; Atom los cuelga de la raiz. Se buscan
        // en todo el arbol para no depender de esa diferencia ni de feeds con
        // envoltorios raros.
        var nodes = document.Root
            .Descendants()
            .Where(node => node.Name.LocalName is "item" or "entry");

        var items = new List<NewsItemDto>();

        foreach (var node in nodes)
        {
            var item = ParseItem(node, sourceName);

            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static NewsItemDto? ParseItem(XElement node, string sourceName)
    {
        var title = Clean(Value(node, "title"));
        var url = ExtractLink(node);

        // Sin titulo no hay que mostrar, y sin enlace la noticia no lleva a la fuente:
        // publicar un extracto que no se puede atribuir es justo lo que no se quiere.
        if (string.IsNullOrEmpty(title) || url is null)
        {
            return null;
        }

        var body = Value(node, "description")
            ?? node.Element(Content + "encoded")?.Value
            ?? Value(node, "summary")
            ?? Value(node, "content");

        return new NewsItemDto(
            title,
            Excerpt(body),
            url,
            ExtractImage(node, body),
            sourceName,
            ExtractDate(node));
    }

    // ------------------------------------------------------------------
    // Campos
    // ------------------------------------------------------------------

    /// <summary>
    /// Busca un hijo por nombre local, sin importar el namespace.
    /// </summary>
    /// <remarks>
    /// Comparar por <see cref="XName"/> completo obligaria a probar el nombre sin
    /// namespace (RSS) y el de Atom para cada campo. Muchos feeds ademas declaran
    /// namespaces propios o los omiten donde no deberian.
    /// </remarks>
    private static string? Value(XElement node, string localName) =>
        node.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    /// <summary>
    /// El enlace al articulo. En RSS es el texto de <c>link</c>; en Atom, el atributo
    /// <c>href</c>, prefiriendo el que se declara como alternativa al propio feed.
    /// </summary>
    private static string? ExtractLink(XElement node)
    {
        var links = node.Elements().Where(child => child.Name.LocalName == "link").ToList();

        var atomHref = links
            .Where(link => link.Attribute("href") is not null)
            .OrderByDescending(link =>
                string.Equals(link.Attribute("rel")?.Value, "alternate", StringComparison.OrdinalIgnoreCase))
            .Select(link => link.Attribute("href")!.Value)
            .FirstOrDefault();

        var candidate = atomHref
            ?? links.Select(link => link.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Value(node, "guid");

        return SafeUrl(candidate);
    }

    private static string? ExtractImage(XElement node, string? body)
    {
        // Por orden de confiabilidad: lo que el feed declara explicitamente como imagen,
        // y recien al final lo que se pueda rescatar del HTML del resumen.
        var fromEnclosure = node.Elements()
            .Where(child => child.Name.LocalName == "enclosure")
            .FirstOrDefault(child =>
                child.Attribute("type")?.Value.StartsWith("image", StringComparison.OrdinalIgnoreCase) == true)
            ?.Attribute("url")?.Value;

        var fromMedia = node.Elements(Media + "content")
            .Concat(node.Elements(Media + "thumbnail"))
            .Select(child => child.Attribute("url")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return SafeUrl(fromEnclosure)
            ?? SafeUrl(fromMedia)
            ?? SafeUrl(FirstImageInHtml(body));
    }

    private static DateTimeOffset? ExtractDate(XElement node)
    {
        var raw = Value(node, "pubDate")
            ?? Value(node, "published")
            ?? Value(node, "updated")
            ?? node.Element(Dc + "date")?.Value;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Una fecha en un formato que no se entiende no descarta la noticia: se muestra
        // sin fecha y queda al final del orden.
        return DateTimeOffset.TryParse(
            raw.Trim(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    // ------------------------------------------------------------------
    // Texto
    // ------------------------------------------------------------------

    /// <summary>
    /// Convierte el resumen —que casi siempre viene con HTML adentro— en un extracto
    /// corto de texto plano.
    /// </summary>
    public static string? Excerpt(string? html)
    {
        var text = Clean(StripHtml(html));

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length <= ExcerptMaxLength)
        {
            return text;
        }

        // Se corta en el ultimo espacio para no partir una palabra al medio. Si no hay
        // ninguno en todo el tramo —una URL larguisima, por ejemplo— se corta seco.
        var cut = text.LastIndexOf(' ', ExcerptMaxLength - 1);

        if (cut < ExcerptMaxLength / 2)
        {
            cut = ExcerptMaxLength - 1;
        }

        return string.Concat(text.AsSpan(0, cut).TrimEnd(), "...");
    }

    /// <summary>
    /// Saca las etiquetas HTML y despues decodifica las entidades.
    /// </summary>
    /// <remarks>
    /// <b>El orden importa.</b> Decodificar primero convertiria <c>&amp;lt;b&amp;gt;</c>
    /// en <c>&lt;b&gt;</c> y el paso siguiente lo borraria como si fuera una etiqueta,
    /// perdiendo texto que el autor escribio a proposito.
    /// </remarks>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(html.Length);
        var insideTag = false;

        foreach (var c in html)
        {
            if (c == '<')
            {
                insideTag = true;
                // El hueco evita que "<b>uno</b><b>dos</b>" quede como "unodos".
                builder.Append(' ');
            }
            else if (c == '>')
            {
                insideTag = false;
            }
            else if (!insideTag)
            {
                builder.Append(c);
            }
        }

        return WebUtility.HtmlDecode(builder.ToString());
    }

    /// <summary>Colapsa los espacios en blanco, saltos de linea incluidos.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string? FirstImageInHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var tag = html.IndexOf("<img", StringComparison.OrdinalIgnoreCase);

        if (tag < 0)
        {
            return null;
        }

        var src = html.IndexOf("src", tag, StringComparison.OrdinalIgnoreCase);

        if (src < 0)
        {
            return null;
        }

        var quote = html.IndexOfAny(['"', '\''], src);

        if (quote < 0)
        {
            return null;
        }

        var end = html.IndexOf(html[quote], quote + 1);

        return end < 0 ? null : WebUtility.HtmlDecode(html[(quote + 1)..end]);
    }

    /// <summary>
    /// Acepta la URL solo si es absoluta y http/https.
    /// </summary>
    /// <remarks>
    /// <b>Esto no es formalismo.</b> El enlace de la noticia termina en un
    /// <c>&lt;a href&gt;</c> del navegador, y el contenido viene de un tercero sobre el
    /// que no se tiene ningun control: un <c>javascript:</c> ahi es ejecucion de codigo
    /// en la sesion del usuario. La imagen se filtra por la misma regla, por coherencia
    /// y porque un <c>data:</c> gigante en el feed tampoco tiene nada que hacer ahi.
    /// </remarks>
    public static string? SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }
}
