using System.ComponentModel.DataAnnotations;

namespace MusicReviews.Infrastructure.Users;

/// <summary>Configuracion de los avatares subidos.</summary>
public sealed class AvatarOptions
{
    public const string SectionName = "Avatars";

    /// <summary>Prefijo de URL con el que se sirven.</summary>
    [Required(AllowEmptyStrings = false)]
    public string RequestPath { get; set; } = "/avatars";

    /// <summary>
    /// Tope de tamanio. Dos megas alcanzan de sobra para una foto de perfil, y el limite
    /// existe para que subir un archivo enorme no sea una forma barata de llenar la base.
    /// </summary>
    [Range(64 * 1024, 20 * 1024 * 1024)]
    public int MaxSizeBytes { get; set; } = 2 * 1024 * 1024;
}
