using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Users;

namespace MusicReviews.Infrastructure.Users;

/// <summary>Configuracion del almacenamiento de avatares.</summary>
public sealed class AvatarOptions
{
    public const string SectionName = "Avatars";

    /// <summary>
    /// Carpeta donde se guardan los archivos. Relativa al directorio de la aplicacion si
    /// no es absoluta.
    /// </summary>
    /// <remarks>
    /// Fuera de <c>wwwroot</c> a proposito: ahi adentro quedaria dentro del publicado y
    /// se perderia en cada despliegue, ademas de mezclar archivos de usuarios con los
    /// del sitio. Se sirve por una ruta estatica propia. Ver <c>Program.cs</c>.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string StoragePath { get; set; } = "data/avatars";

    /// <summary>Prefijo de URL con el que se sirven.</summary>
    [Required(AllowEmptyStrings = false)]
    public string RequestPath { get; set; } = "/avatars";

    /// <summary>
    /// Tope de tamanio. Dos megas alcanzan de sobra para una foto de perfil, y el limite
    /// existe para que subir un archivo enorme no sea una forma barata de llenar el disco.
    /// </summary>
    [Range(64 * 1024, 20 * 1024 * 1024)]
    public int MaxSizeBytes { get; set; } = 2 * 1024 * 1024;
}

/// <inheritdoc cref="IAvatarStorage"/>
internal sealed class AvatarStorage : IAvatarStorage
{
    private readonly IOptionsMonitor<AvatarOptions> _options;
    private readonly ILogger<AvatarStorage> _logger;

    public AvatarStorage(IOptionsMonitor<AvatarOptions> options, ILogger<AvatarStorage> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Guid userId,
        ImageFormat format,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;

        Directory.CreateDirectory(options.StoragePath);

        // El nombre lo genera el servidor, nunca el cliente. Un nombre de archivo que
        // viene de afuera es la via clasica de escribir fuera de la carpeta con "../",
        // y ademas el sufijo aleatorio hace que la foto anterior deje de resolver en
        // cuanto se reemplaza, sin depender de que ninguna cache haga caso.
        var fileName = $"{userId:N}-{Guid.NewGuid():N}{ImageSignature.ExtensionFor(format)}";
        var fullPath = Path.Combine(options.StoragePath, fileName);

        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return $"{options.RequestPath.TrimEnd('/')}/{fileName}";
    }

    public void DeleteIfOwned(string? url)
    {
        var options = _options.CurrentValue;
        var prefix = options.RequestPath.TrimEnd('/') + "/";

        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        var fileName = Path.GetFileName(url);

        // Se vuelve a tomar solo el nombre del archivo aunque la URL ya tenga el prefijo
        // esperado: es lo unico que garantiza que no se salga de la carpeta.
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(options.StoragePath, fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Es limpieza: que quede un archivo huerfano no puede impedir que el usuario
            // cambie su foto.
            _logger.LogWarning(ex, "No se pudo borrar el avatar anterior {File}", fileName);
        }
    }
}
