using System.Security.Claims;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Infrastructure.Identity;

namespace MusicReviews.Api.Infrastructure;

/// <summary>
/// Lee el usuario autenticado de los claims de la request en curso.
/// Vive en la capa Api porque es la unica que conoce <see cref="HttpContext"/>.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(AuthClaimTypes.Sub), out var id)
            ? id
            : null;

    public string? UserName => Principal?.FindFirstValue(AuthClaimTypes.Name);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
