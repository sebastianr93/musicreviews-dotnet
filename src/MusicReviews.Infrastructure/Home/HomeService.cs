using Microsoft.EntityFrameworkCore;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Home;
using MusicReviews.Application.Home.Dtos;
using MusicReviews.Domain.Enums;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Home;

/// <inheritdoc cref="IHomeService"/>
internal sealed class HomeService : IHomeService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public HomeService(AppDbContext context, ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    // ------------------------------------------------------------------
    // Populares por actividad
    // ------------------------------------------------------------------

    /// <remarks>
    /// <para>
    /// <b>Se cuenta desde la actividad, no desde los albumes.</b> La forma directa
    /// —recorrer los albumes y contarle a cada uno su movimiento con subconsultas— toca
    /// todo el catalogo para descubrir que la mayoria no tuvo ninguno. Aca se hace al
    /// reves: cada consulta arranca de las filas que ocurrieron dentro de la ventana,
    /// que son pocas y estan indexadas por fecha, y agrupa por album.
    /// </para>
    /// <para>
    /// <b>Tres consultas y la mezcla en memoria</b>, igual que en el timeline de
    /// actividad y por la misma razon: son tres agregaciones que el motor resuelve con
    /// su propio indice, y un UNION las dejaria sin poder usarlo. Los votos necesitan un
    /// join explicito contra reseñas porque <c>Like</c> es polimorfico y no tiene FK.
    /// </para>
    /// <para>
    /// <b>Lo que esto no es.</b> El resultado no esta acotado por el tamaño de la
    /// pagina sino por la ventana: si el sitio tuviera mucho trafico, treinta dias de
    /// actividad no entran comodamente en memoria y esto habria que reemplazarlo por un
    /// contador materializado que se actualice con cada accion. Para el volumen de este
    /// proyecto la version derivada es preferible: no hay nada que sincronizar y borrar
    /// una reseña le quita su peso sin que nadie tenga que acordarse.
    /// </para>
    /// </remarks>
    public async Task<Result<PagedResult<HomeAlbumDto>>> GetPopularAsync(
        PageRequest page,
        int windowDays = HomeDefaults.PopularWindowDays,
        CancellationToken cancellationToken = default)
    {
        var days = Math.Clamp(windowDays, HomeDefaults.MinWindowDays, HomeDefaults.MaxWindowDays);
        var since = _timeProvider.GetUtcNow().AddDays(-days);

        var reviewActivity = await _context.Reviews
            .Where(r => r.CreatedAt >= since)
            .GroupBy(r => r.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Los comentarios borrados no cuentan: el hilo sigue mostrando el hueco, pero
        // el album no deberia seguir cobrando popularidad por algo que ya no se lee.
        var commentActivity = await _context.Comments
            .Where(c => c.CreatedAt >= since && !c.IsDeleted)
            .GroupBy(c => c.Review.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var voteActivity = await _context.Likes
            .Where(l => l.TargetType == LikeTargetType.Review && l.CreatedAt >= since)
            .Join(_context.Reviews, like => like.TargetId, review => review.Id,
                (like, review) => review.AlbumId)
            .GroupBy(albumId => albumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var scores = new Dictionary<int, int>();

        void Accumulate(int albumId, int count, int weight) =>
            scores[albumId] = scores.GetValueOrDefault(albumId) + (count * weight);

        foreach (var row in reviewActivity)
        {
            Accumulate(row.AlbumId, row.Count, HomeDefaults.ReviewWeight);
        }

        foreach (var row in commentActivity)
        {
            Accumulate(row.AlbumId, row.Count, HomeDefaults.CommentWeight);
        }

        foreach (var row in voteActivity)
        {
            Accumulate(row.AlbumId, row.Count, HomeDefaults.VoteWeight);
        }

        if (scores.Count == 0)
        {
            return Result.Success(PagedResult<HomeAlbumDto>.Empty(page.Page, page.PageSize));
        }

        // El desempate por Id no es cosmetico: sin el, dos albumes con el mismo puntaje
        // pueden intercambiar posiciones entre la pagina 1 y la 2 y aparecer repetidos
        // o desaparecer.
        var ordered = scores
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Select(entry => entry.Key)
            .ToList();

        var pageIds = ordered.Skip(page.Offset).Take(page.PageSize).ToList();

        var albums = await LoadAlbumsAsync(pageIds, cancellationToken);

        // LoadAlbumsAsync devuelve en el orden de la base; el orden que vale es el del
        // ranking, asi que se reordena por la posicion que ya se decidio.
        var items = pageIds
            .Where(albums.ContainsKey)
            .Select(id => albums[id] with { ActivityScore = scores[id] })
            .ToList();

        return Result.Success(new PagedResult<HomeAlbumDto>(
            items, page.Page, page.PageSize, ordered.Count));
    }

    // ------------------------------------------------------------------
    // Explorar el catalogo
    // ------------------------------------------------------------------

    /// <remarks>
    /// <para>
    /// El orden es <b>lo ultimo que entro al catalogo</b>. El catalogo local no se
    /// precarga: un album esta aca porque alguien lo busco, asi que "lo mas nuevo" es
    /// literalmente lo que la gente estuvo mirando, y ademas cambia solo con el uso, que
    /// es lo que hace que la seccion no muestre siempre lo mismo.
    /// </para>
    /// <para>
    /// <b>Se ordena por Id descendente y no por <c>CachedAt</c>.</b> Los dos parecen
    /// decir lo mismo, pero <c>CachedAt</c> se actualiza cada vez que una ficha se
    /// refresca contra MusicBrainz: con el, un album de 2003 que alguien abrio hoy
    /// saltaria al principio de "lo ultimo que entro", que es justo lo contrario de lo
    /// que la seccion promete. El Id es la fecha de alta y no se mueve nunca.
    /// </para>
    /// <para>
    /// Los que tienen portada van primero. No es capricho: la seccion es una grilla de
    /// tapas grandes, y una fila de recuadros vacios no invita a explorar nada.
    /// </para>
    /// <para>
    /// Se pagina por offset y no por cursor, al reves que los feeds. La diferencia es
    /// que un feed crece por arriba —lo nuevo entra en la primera posicion y corre todo
    /// un lugar, y el offset repite filas—, mientras que este orden se mueve a la
    /// velocidad a la que alguien cachea un album nuevo, no a la de un scroll.
    /// </para>
    /// </remarks>
    public async Task<Result<PagedResult<HomeAlbumDto>>> GetExploreAsync(
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Albums.AsNoTracking();

        if (_currentUser.UserId is { } userId)
        {
            // Recomendarle lo que ya escucho y puntuo no descubre nada.
            query = query.Where(a => !a.Reviews.Any(r => r.UserId == userId));
        }

        var total = await query.CountAsync(cancellationToken);

        var ids = await query
            .OrderByDescending(a => a.CoverArtUrl != null)
            .ThenByDescending(a => a.Id)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var albums = await LoadAlbumsAsync(ids, cancellationToken);

        var items = ids.Where(albums.ContainsKey).Select(id => albums[id]).ToList();

        return Result.Success(new PagedResult<HomeAlbumDto>(
            items, page.Page, page.PageSize, total));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Trae los albumes de la pagina con sus estadisticas en UNA sola consulta.
    /// </summary>
    /// <remarks>
    /// Las dos secciones deciden primero <i>que</i> albumes mostrar y recien despues
    /// piden sus datos. Separarlo evita el N+1 obvio —una consulta por tarjeta— y
    /// tambien uno menos obvio: calcular promedio y cantidad de reseñas durante el
    /// ranking obligaria a computarlos para todo el catalogo y no solo para las quince
    /// filas que se van a mostrar.
    /// </remarks>
    private async Task<Dictionary<int, HomeAlbumDto>> LoadAlbumsAsync(
        List<int> albumIds,
        CancellationToken cancellationToken)
    {
        if (albumIds.Count == 0)
        {
            return [];
        }

        return await _context.Albums
            .AsNoTracking()
            .Where(a => albumIds.Contains(a.Id))
            .Select(a => new HomeAlbumDto(
                a.Id,
                a.MusicBrainzId,
                a.Title,
                a.CoverArtUrl,
                a.Artist.Name,
                a.Artist.MusicBrainzId,
                a.ReleaseDate,
                a.Reviews.Count(),
                a.Reviews.Any() ? a.Reviews.Average(r => (double)r.Score) : null,
                0))
            .ToDictionaryAsync(album => album.Id, cancellationToken);
    }
}
