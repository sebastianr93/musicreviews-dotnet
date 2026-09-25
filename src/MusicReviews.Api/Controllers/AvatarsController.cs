using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicReviews.Application.Users;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Sirve las fotos de perfil guardadas en la base.
/// </summary>
/// <remarks>
/// <para>
/// Antes de esto los avatares eran archivos y los servia <c>UseStaticFiles</c> apuntando a
/// una carpeta. Al pasar a la base hace falta un endpoint, y con el vuelven a hacer falta
/// —a mano— las dos defensas que el middleware de estaticos daba por su cuenta.
/// </para>
/// <para>
/// <b>El Content-Type sale de la fila, no de la URL.</b> Se guardo a partir de los bytes
/// magicos en el momento de subir, asi que la extension de la URL es decorativa: sirve
/// para que el archivo se vea con nombre razonable si alguien lo descarga, y nada mas.
/// </para>
/// <para>
/// <b>nosniff sigue siendo obligatorio.</b> Sin esa cabecera el navegador puede ignorar el
/// Content-Type y decidir por su cuenta que la respuesta es HTML. Servido desde nuestro
/// propio dominio, eso es un XSS con nuestro origen. La validacion por bytes al subir y
/// esta cabecera al servir son las dos mitades de la misma defensa.
/// </para>
/// <para>
/// La ruta no lleva el prefijo <c>api/</c> a proposito: es la que quedo publicada en los
/// perfiles que ya existen, y cambiarla dejaria esas URL rotas.
/// </para>
/// </remarks>
[ApiController]
[Route("avatars")]
[AllowAnonymous]
public class AvatarsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public AvatarsController(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    /// <summary>Devuelve la imagen. Publica: un avatar se ve en cualquier perfil publico.</summary>
    /// <param name="fileName">Nombre con el que se publico: 32 hexadecimales y la extension.</param>
    [HttpGet("{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string fileName, CancellationToken cancellationToken)
    {
        if (AvatarUrl.ParseFileName(fileName) is not { } avatarId)
        {
            return NotFound();
        }

        var avatar = await _context.UserAvatars
            .AsNoTracking()
            .Where(entity => entity.Id == avatarId)
            .Select(entity => new { entity.Content, entity.ContentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (avatar is null)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";

        // El Id cambia con cada subida, asi que esta URL no puede devolver otra cosa
        // nunca mas: es cacheable de forma indefinida. En desarrollo se revalida, por el
        // mismo motivo que el resto de los estaticos.
        Response.Headers.CacheControl = _environment.IsDevelopment()
            ? "no-cache, must-revalidate"
            : "public, max-age=31536000, immutable";

        return File(avatar.Content, avatar.ContentType);
    }
}
