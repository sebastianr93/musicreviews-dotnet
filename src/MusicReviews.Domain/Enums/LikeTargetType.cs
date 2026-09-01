namespace MusicReviews.Domain.Enums;

/// <summary>
/// Tipo de entidad sobre la que se emite un voto.
/// Se persiste como <c>int</c>: los valores explicitos evitan que reordenar
/// el enum en el futuro corrompa los datos ya guardados.
/// </summary>
public enum LikeTargetType
{
    Review = 1,
    Comment = 2
}
