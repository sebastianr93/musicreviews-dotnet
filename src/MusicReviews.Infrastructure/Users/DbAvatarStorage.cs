using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Users;
using MusicReviews.Domain.Entities;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Users;

/// <summary>
/// Guarda los avatares como filas de la base.
/// </summary>
/// <remarks>
/// <para>
/// La alternativa natural es el disco, y fue la primera implementacion. Se cambio al
/// elegir el destino de despliegue: en un contenedor sin volumen —Cloud Run, y el plan
/// gratuito de casi todas las plataformas— el filesystem es efimero y se pierde en cada
/// version nueva y en cada arranque en frio. El sintoma seria de los peores: la
/// aplicacion funciona, nadie ve un error, y las fotos de perfil desaparecen solas cada
/// tanto.
/// </para>
/// <para>
/// El costo es real y esta acotado: cada avatar pesa como maximo lo que permita
/// <see cref="AvatarOptions.MaxSizeBytes"/>, se lee unicamente cuando alguien pide la
/// imagen —vive en su propia tabla, no en una columna de <c>AspNetUsers</c>— y la URL
/// cambia con cada subida, asi que se puede cachear para siempre.
/// </para>
/// </remarks>
internal sealed class DbAvatarStorage : IAvatarStorage
{
    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<AvatarOptions> _options;

    public DbAvatarStorage(AppDbContext context, IOptionsMonitor<AvatarOptions> options)
    {
        _context = context;
        _options = options;
    }

    public async Task<string> SaveAsync(
        Guid userId,
        ImageFormat format,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var avatar = new UserAvatar
        {
            // El Id se genera aca y no en la base porque forma parte de la URL que hay
            // que devolver en esta misma llamada, antes de que se confirme la escritura.
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = buffer.ToArray(),
            ContentType = ImageSignature.ContentTypeFor(format),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.UserAvatars.Add(avatar);

        // Sin SaveChangesAsync: lo confirma quien llama, junto con el AvatarUrl del
        // usuario. Ver el comentario de IAvatarStorage.SaveAsync.
        return AvatarUrl.For(_options.CurrentValue.RequestPath, avatar.Id, format);
    }

    public async Task DeleteIfOwnedAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (AvatarUrl.IdFrom(_options.CurrentValue.RequestPath, url) is not { } avatarId)
        {
            return;
        }

        // ExecuteDelete y no Remove: es una sola fila identificada por su clave, no hace
        // falta traerla —son hasta dos megas— para poder borrarla.
        await _context.UserAvatars
            .Where(avatar => avatar.Id == avatarId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
