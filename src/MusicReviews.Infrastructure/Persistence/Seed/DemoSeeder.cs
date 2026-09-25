using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Common.Models;
using MusicReviews.Domain.Entities;
using MusicReviews.Domain.Enums;

namespace MusicReviews.Infrastructure.Persistence.Seed;

/// <summary>
/// Llena la instancia publica con catalogo, usuarios y actividad para que el home no
/// aparezca vacio la primera vez.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que es un servicio en segundo plano y no un paso del arranque.</b> Sembrar
/// veinte albumes son unas cuarenta peticiones a MusicBrainz, y el limite es de una por
/// segundo: casi un minuto. Hacerlo antes de empezar a escuchar significa que la
/// plataforma no recibe respuesta al health check durante ese minuto, da el despliegue
/// por fallido y reinicia el contenedor —que vuelve a sembrar desde cero, y otra vez.
/// Asi la aplicacion responde desde el primer segundo y el catalogo va apareciendo.
/// </para>
/// <para>
/// <b>Por que se busca en vez de usar MBIDs fijos.</b> Un MBID escrito a mano que este
/// mal no falla ruidosamente: devuelve 404, el album no se siembra y el resultado es un
/// catalogo incompleto que nadie nota. Buscando por artista y titulo, el identificador
/// lo resuelve MusicBrainz y de paso se reusa toda la maquinaria que ya existe —el
/// throttling, los reintentos, la portada, el guardado en la base—.
/// </para>
/// <para>
/// <b>Idempotente.</b> La condicion de corte es que ya existan las cuentas de
/// demostracion. Un reinicio no duplica nada.
/// </para>
/// </remarks>
public sealed class DemoSeeder : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptions<DemoOptions> _options;
    private readonly ILogger<DemoSeeder> _logger;

    public DemoSeeder(IServiceProvider services, IOptions<DemoOptions> options, ILogger<DemoSeeder> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Datos
    // ------------------------------------------------------------------

    private sealed record DemoUser(string UserName, string Email, string Bio);

    private sealed record DemoAlbum(string Artist, string Title);

    private static readonly DemoUser[] Users =
    [
        new("celeste", "celeste@demo.musicreviews.app",
            "Escucho de todo y opino de casi todo. Debilidad por el post-punk."),
        new("bruno", "bruno@demo.musicreviews.app",
            "Discos enteros, en orden, de principio a fin. No creo en el shuffle."),
        new("lucia", "lucia@demo.musicreviews.app",
            "Jazz, soul y cualquier cosa con una linea de bajo que valga la pena."),
        new("tomas", "tomas@demo.musicreviews.app",
            "Puntajes altos con criterio. Casi siempre."),
        new("mora", "mora@demo.musicreviews.app",
            "Electronica, ambient y bandas sonoras. Escribo largo, aviso."),
        new("ivan", "ivan@demo.musicreviews.app",
            "Rock argentino y todo lo que salio de ahi.")
    ];

    /// <summary>
    /// Discos elegidos para que el catalogo inicial se vea variado: distintas decadas,
    /// distintos generos y portadas reconocibles, que es lo que sostiene visualmente un
    /// home hecho de tapas grandes.
    /// </summary>
    private static readonly DemoAlbum[] Albums =
    [
        new("Radiohead", "OK Computer"),
        new("Radiohead", "In Rainbows"),
        new("Pink Floyd", "The Dark Side of the Moon"),
        new("Fleetwood Mac", "Rumours"),
        new("Kendrick Lamar", "To Pimp a Butterfly"),
        new("Kendrick Lamar", "good kid, m.A.A.d city"),
        new("Miles Davis", "Kind of Blue"),
        new("John Coltrane", "A Love Supreme"),
        new("Lauryn Hill", "The Miseducation of Lauryn Hill"),
        new("Daft Punk", "Discovery"),
        new("Aphex Twin", "Selected Ambient Works 85-92"),
        new("Portishead", "Dummy"),
        new("Joy Division", "Unknown Pleasures"),
        new("The Velvet Underground", "The Velvet Underground & Nico"),
        new("David Bowie", "Hunky Dory"),
        new("Talking Heads", "Remain in Light"),
        new("Soda Stereo", "Cancion Animal"),
        new("Charly Garcia", "Clics Modernos"),
        new("Spinetta Jade", "Alma de Diamante"),
        new("Gustavo Cerati", "Bocanada"),
        new("Bjork", "Homogenic"),
        new("Massive Attack", "Mezzanine"),
        new("Frank Ocean", "Blonde"),
        new("Amy Winehouse", "Back to Black"),
        new("My Bloody Valentine", "Loveless"),
        new("Nirvana", "In Utero"),
        new("Wu-Tang Clan", "Enter the Wu-Tang (36 Chambers)"),
        new("Stevie Wonder", "Innervisions")
    ];

    /// <summary>
    /// Reseñas genericas pero escritas: el objetivo es que quien entra lea algo que
    /// parece una reseña, no "Lorem ipsum" ni "review de prueba 1".
    /// </summary>
    private static readonly string[] ReviewTexts =
    [
        "Lo puse esperando escuchar dos temas y me quede hasta el final. Tiene ese ritmo raro de los discos que estan pensados como una sola pieza: sacas una cancion del medio y lo que queda no cierra igual.",
        "Me costo la primera vuelta. A la tercera entendi que el problema era mio: estaba esperando estribillos donde el disco propone otra cosa. Sigue creciendo con cada escucha.",
        "La produccion es lo primero que llama la atencion y despues pasa a segundo plano, que es exactamente lo que tiene que hacer. Nada suena a decoracion.",
        "No es perfecto. Hay dos temas en la segunda mitad que no aportan y alargan algo que ya estaba dicho. Aun asi lo vuelvo a poner cada un par de meses.",
        "De esos discos que uno cree conocer por escuchar los singles sueltos y despues descubre que no tenian nada que ver con el resto.",
        "Envejecio mejor de lo que se esperaba. Lo que en su momento sonaba a exceso hoy suena a decision.",
        "Tiene un final que reordena todo lo anterior. Vale la pena llegar hasta ahi antes de decidir que te parece.",
        "Lo escuche caminando y funciona; lo escuche con auriculares y es otro disco. No muchos aguantan las dos cosas.",
        "El mejor argumento a favor es que despues de terminarlo cuesta poner otra cosa. Deja el oido ocupado.",
        "Sobrevalorado no es la palabra, pero si esta rodeado de una mitologia que no lo ayuda. Escuchado sin todo eso encima es muy bueno igual."
    ];

    private static readonly string[] CommentTexts =
    [
        "Coincido con casi todo, salvo con lo de la segunda mitad: para mi ahi esta lo mejor.",
        "Buena reseña. Me diste ganas de volver a escucharlo con otra cabeza.",
        "Lo mismo me paso a mi la primera vez. Dale una vuelta mas.",
        "Yo le pondria un poco menos, pero entiendo el puntaje.",
        "Nunca lo habia pensado asi. Anotado.",
        "Justo lo estaba escuchando cuando lei esto."
    ];

    private static readonly string[] ReplyTexts =
    [
        "Puede ser, es la parte mas discutida del disco.",
        "Contame que te parece cuando lo termines.",
        "Es la escucha que mas cambia entre la primera y la quinta vez, sin dudas."
    ];

    // ------------------------------------------------------------------
    // Ejecucion
    // ------------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;

        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            _logger.LogWarning(
                "El contenido de demostracion esta activado pero falta Demo:Password. No se siembra nada.");
            return;
        }

        try
        {
            await SeedAsync(options, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // La aplicacion se esta apagando. No es un error.
        }
        catch (Exception ex)
        {
            // Una siembra a medias no puede tumbar la aplicacion: el catalogo se llena
            // solo con el uso normal, asi que lo peor que pasa es que el home arranque
            // mas vacio de lo previsto.
            _logger.LogError(ex, "Fallo la siembra del contenido de demostracion.");
        }
    }

    private async Task SeedAsync(DemoOptions options, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var catalog = scope.ServiceProvider.GetRequiredService<IMusicCatalogService>();

        // En una variable local y no indexando el arreglo dentro del lambda: EF traduce
        // el arbol de expresion y un indexador ahi lo obliga a evaluar del lado del cliente.
        var markerUserName = Users[0].UserName;

        if (await context.Users.AnyAsync(user => user.UserName == markerUserName, cancellationToken))
        {
            _logger.LogInformation("El contenido de demostracion ya existe.");
            return;
        }

        var people = await CreateUsersAsync(userManager, options.Password!, cancellationToken);

        if (people.Count == 0)
        {
            return;
        }

        await CreateFollowsAsync(context, people, cancellationToken);

        var albums = await SeedCatalogAsync(catalog, context, options.MaxAlbums, cancellationToken);

        _logger.LogInformation("Catalogo de demostracion: {Count} albumes.", albums.Count);

        if (albums.Count > 0)
        {
            await CreateActivityAsync(context, people, albums, cancellationToken);
        }

        _logger.LogInformation("Contenido de demostracion listo.");
    }

    private async Task<List<ApplicationUser>> CreateUsersAsync(
        UserManager<ApplicationUser> userManager,
        string password,
        CancellationToken cancellationToken)
    {
        var created = new List<ApplicationUser>();
        var createdAt = DateTimeOffset.UtcNow.AddDays(-45);

        foreach (var person in Users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var user = new ApplicationUser
            {
                UserName = person.UserName,
                Email = person.Email,
                EmailConfirmed = true,
                Bio = person.Bio,
                CreatedAt = createdAt
            };

            var result = await userManager.CreateAsync(user, password);

            if (result.Succeeded)
            {
                created.Add(user);
                createdAt = createdAt.AddDays(2);
            }
            else
            {
                _logger.LogWarning(
                    "No se pudo crear el usuario de demostracion {UserName}: {Errors}",
                    person.UserName,
                    string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }

        return created;
    }

    /// <summary>
    /// Teje la red de seguidores. Sin esto el feed de "a quienes seguis" queda vacio
    /// aunque haya actividad de sobra.
    /// </summary>
    private static async Task CreateFollowsAsync(
        AppDbContext context,
        List<ApplicationUser> people,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.AddDays(-30);

        for (var i = 0; i < people.Count; i++)
        {
            // Cada uno sigue a los dos siguientes en la rueda: todos terminan con
            // seguidores y seguidos, y ningun perfil queda aislado.
            for (var step = 1; step <= 2; step++)
            {
                context.UserFollows.Add(new UserFollow
                {
                    FollowerId = people[i].Id,
                    FollowedId = people[(i + step) % people.Count].Id,
                    CreatedAt = now.AddHours(i * 3 + step)
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Trae los albumes desde MusicBrainz. Devuelve los Id locales de los que quedaron
    /// guardados.
    /// </summary>
    private async Task<List<int>> SeedCatalogAsync(
        IMusicCatalogService catalog,
        AppDbContext context,
        int maxAlbums,
        CancellationToken cancellationToken)
    {
        var seeded = new List<int>();

        foreach (var album in Albums.Take(Math.Clamp(maxAlbums, 0, Albums.Length)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var search = await catalog.SearchAlbumsAsync(
                $"{album.Artist} {album.Title}",
                new PageRequest(1, 5),
                cancellationToken);

            if (!search.IsSuccess || search.Value.Items.Count == 0)
            {
                _logger.LogWarning("Sin resultados para {Artist} - {Title}.", album.Artist, album.Title);
                continue;
            }

            // El primero es el que la busqueda considera mas relevante; el orden ya
            // pondera coincidencia y cantidad de ediciones.
            var match = search.Value.Items[0];

            // GetAlbumAsync es lo que persiste el album y su artista: la busqueda sola
            // no guarda nada.
            var detail = await catalog.GetAlbumAsync(match.MusicBrainzId, cancellationToken);

            if (!detail.IsSuccess)
            {
                _logger.LogWarning("No se pudo traer el detalle de {Title}.", album.Title);
                continue;
            }

            var id = await context.Albums
                .Where(entity => entity.MusicBrainzId == match.MusicBrainzId)
                .Select(entity => entity.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (id != 0)
            {
                seeded.Add(id);
            }
        }

        return seeded;
    }

    /// <summary>
    /// Reseñas, comentarios, respuestas y votos, con fechas escalonadas hacia atras.
    /// </summary>
    /// <remarks>
    /// Las fechas importan mas de lo que parece: el home ordena por actividad reciente y
    /// la seccion de populares mira una ventana de treinta dias. Si todo se creara con la
    /// misma marca de tiempo, las dos secciones mostrarian el mismo bloque en el mismo
    /// orden y el feed se veria artificial.
    /// </remarks>
    private static async Task CreateActivityAsync(
        AppDbContext context,
        List<ApplicationUser> people,
        List<int> albums,
        CancellationToken cancellationToken)
    {
        // Semilla fija: la instancia se puede volver a sembrar y queda igual, lo que
        // hace que una captura de pantalla siga siendo valida.
        var random = new Random(20260907);
        var now = DateTimeOffset.UtcNow;

        var reviews = new List<Review>();

        foreach (var albumId in albums)
        {
            // Entre dos y cuatro reseñas por album, de usuarios distintos: el indice
            // unico (UserId, AlbumId) rechaza repetir autor.
            var authors = people.OrderBy(_ => random.Next()).Take(random.Next(2, 5)).ToList();

            foreach (var author in authors)
            {
                reviews.Add(new Review
                {
                    AlbumId = albumId,
                    UserId = author.Id,
                    Score = random.Next(58, 99),
                    Text = ReviewTexts[random.Next(ReviewTexts.Length)],
                    CreatedAt = now.AddHours(-random.Next(1, 24 * 25))
                });
            }
        }

        context.Reviews.AddRange(reviews);
        await context.SaveChangesAsync(cancellationToken);

        var comments = new List<Comment>();

        // Solo una parte de las reseñas tiene conversacion: que todas tengan hilo se
        // nota tanto como que ninguna lo tenga.
        foreach (var review in reviews.OrderBy(_ => random.Next()).Take(reviews.Count / 2))
        {
            foreach (var author in people.Where(person => person.Id != review.UserId)
                         .OrderBy(_ => random.Next())
                         .Take(random.Next(1, 4)))
            {
                comments.Add(new Comment
                {
                    ReviewId = review.Id,
                    UserId = author.Id,
                    Depth = 0,
                    Text = CommentTexts[random.Next(CommentTexts.Length)],
                    CreatedAt = review.CreatedAt.AddHours(random.Next(1, 48))
                });
            }
        }

        context.Comments.AddRange(comments);
        await context.SaveChangesAsync(cancellationToken);

        // Respuestas: un segundo nivel alcanza para que se vea que el hilo es un arbol
        // y no una lista.
        var replies = comments
            .OrderBy(_ => random.Next())
            .Take(comments.Count / 3)
            .Select(parent => new Comment
            {
                ReviewId = parent.ReviewId,
                UserId = reviews.First(review => review.Id == parent.ReviewId).UserId,
                ParentCommentId = parent.Id,
                Depth = 1,
                Text = ReplyTexts[random.Next(ReplyTexts.Length)],
                CreatedAt = parent.CreatedAt.AddHours(random.Next(1, 20))
            })
            .ToList();

        context.Comments.AddRange(replies);
        await context.SaveChangesAsync(cancellationToken);

        var likes = new List<Like>();

        void Vote(LikeTargetType type, int targetId, Guid excludedAuthor, DateTimeOffset createdAt)
        {
            foreach (var voter in people.Where(person => person.Id != excludedAuthor)
                         .OrderBy(_ => random.Next())
                         .Take(random.Next(0, people.Count)))
            {
                likes.Add(new Like
                {
                    UserId = voter.Id,
                    TargetType = type,
                    TargetId = targetId,
                    // Mayoria de positivos, con algun negativo: un contador que solo
                    // sube no muestra que el voto tiene dos direcciones.
                    IsLike = random.Next(10) > 1,
                    CreatedAt = createdAt.AddHours(random.Next(1, 72))
                });
            }
        }

        foreach (var review in reviews)
        {
            Vote(LikeTargetType.Review, review.Id, review.UserId, review.CreatedAt);
        }

        foreach (var comment in comments)
        {
            Vote(LikeTargetType.Comment, comment.Id, comment.UserId, comment.CreatedAt);
        }

        context.Likes.AddRange(likes);
        await context.SaveChangesAsync(cancellationToken);
    }
}
