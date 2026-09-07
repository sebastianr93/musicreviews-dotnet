using Microsoft.EntityFrameworkCore;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Search;
using MusicReviews.Application.Search.Dtos;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Search;

/// <inheritdoc cref="IQuickSearchService"/>
internal sealed class QuickSearchService : IQuickSearchService
{
    /// <summary>
    /// Calidad del match textual. Manda sobre la popularidad: quien escribe "nir" quiere
    /// lo que empieza con "nir", no el disco mas resenado que en algun lado contiene esas
    /// letras.
    /// </summary>
    private const int MatchExact = 3;
    private const int MatchPrefix = 2;
    private const int MatchContains = 1;

    /// <summary>
    /// Cuanto pesa cada tipo de actividad en la popularidad local. Un favorito es una
    /// decision explicita sobre el artista; una resenia es trabajo que alguien se tomo.
    /// Que el disco este cacheado, en cambio, solo dice que alguien lo abrio una vez.
    /// </summary>
    private const int FavoriteWeight = 5;
    private const int ReviewWeight = 5;

    private readonly AppDbContext _context;

    public QuickSearchService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<IReadOnlyList<QuickSearchItemDto>>> SearchAsync(
        string query,
        int limit = QuickSearchDefaults.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var term = query?.Trim() ?? string.Empty;

        if (term.Length < QuickSearchDefaults.MinQueryLength)
        {
            return Result.Success<IReadOnlyList<QuickSearchItemDto>>([]);
        }

        var take = Math.Clamp(limit, 1, QuickSearchDefaults.MaxLimit);
        var contains = $"%{EscapeLike(term)}%";
        var prefix = $"{EscapeLike(term)}%";

        // Se piden hasta `take` de cada tipo y se mezclan despues. Pedir `take` en total
        // repartido dejaria que un tipo con muchos resultados tape al otro por completo,
        // y el desplegable tiene que poder mostrar el artista Y sus discos.
        // El orden y el recorte van ANTES de proyectar. Al reves no compila en SQL:
        // QuickSearchItemDto es un record posicional y EF no puede mapear sus
        // propiedades de vuelta a columnas para armar el ORDER BY. Es el mismo tropiezo
        // que ya se documento en los favoritos.
        var artists = await _context.Artists
            .AsNoTracking()
            .Where(a => EF.Functions.ILike(a.Name, contains))
            .OrderByDescending(a => EF.Functions.ILike(a.Name, prefix))
            .ThenByDescending(a => (a.FavoritedBy.Count() * FavoriteWeight) + a.Albums.Count())
            .ThenBy(a => a.Name)
            .Take(take)
            .Select(a => new QuickSearchItemDto(
                QuickSearchKind.Artist,
                a.MusicBrainzId,
                a.Name,
                a.Disambiguation ?? a.Type,
                a.ImageUrl,
                null,
                (a.FavoritedBy.Count() * FavoriteWeight) + a.Albums.Count()))
            .ToListAsync(cancellationToken);

        var albums = await _context.Albums
            .AsNoTracking()
            .Where(a => EF.Functions.ILike(a.Title, contains))
            .OrderByDescending(a => EF.Functions.ILike(a.Title, prefix))
            .ThenByDescending(a => a.Reviews.Count())
            .ThenBy(a => a.Title)
            .Take(take)
            .Select(a => new QuickSearchItemDto(
                QuickSearchKind.Album,
                a.MusicBrainzId,
                a.Title,
                a.Artist.Name,
                a.CoverArtUrl,
                a.ReleaseDate == null ? (int?)null : a.ReleaseDate.Value.Year,
                a.Reviews.Count() * ReviewWeight))
            .ToListAsync(cancellationToken);

        // La mezcla final va en memoria: son a lo sumo cuarenta filas y ordenarlas aca
        // evita el UNION de dos proyecciones distintas, que dejaria al motor sin poder
        // usar los indices de cada tabla para su propio ORDER BY.
        var merged = artists
            .Concat(albums)
            .OrderByDescending(item => MatchQuality(item.Title, term))
            .ThenByDescending(item => item.Popularity)
            // A igualdad de todo, el titulo corto primero: "Nirvana" antes que
            // "Nirvana Tribute Band", que es lo que la persona estaba buscando.
            .ThenBy(item => item.Title.Length)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

        return Result.Success<IReadOnlyList<QuickSearchItemDto>>(merged);
    }

    private static int MatchQuality(string title, string term)
    {
        if (string.Equals(title, term, StringComparison.OrdinalIgnoreCase))
        {
            return MatchExact;
        }

        return title.StartsWith(term, StringComparison.OrdinalIgnoreCase)
            ? MatchPrefix
            : MatchContains;
    }

    /// <summary>
    /// Escapa los comodines de <c>LIKE</c>.
    /// </summary>
    /// <remarks>
    /// Sin esto, escribir "%" en el buscador hace que el patron quede en <c>%%%</c> y
    /// devuelva el catalogo entero, y un "_" pasa a valer como "cualquier caracter".
    /// No es una inyeccion —el valor viaja como parametro—, pero si un resultado
    /// desconcertante para quien escribio un simbolo cualquiera.
    /// </remarks>
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
