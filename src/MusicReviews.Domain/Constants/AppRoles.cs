namespace MusicReviews.Domain.Constants;

/// <summary>
/// Roles de la aplicacion. Se siembran al arrancar y se usan en los atributos
/// <c>[Authorize(Roles = ...)]</c> de la capa Api.
/// </summary>
public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [User, Admin];
}
