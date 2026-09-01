using MusicReviews.Application.Admin.Dtos;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;

namespace MusicReviews.Application.Admin;

public interface IAdminService
{
    Task<Result<AdminStatsDto>> GetStatsAsync(CancellationToken cancellationToken = default);

    Task<Result<PagedResult<AdminUserDto>>> GetUsersAsync(
        PageRequest page,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>Asigna un rol. Idempotente.</summary>
    Task<Result<AdminUserDto>> AssignRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quita un rol. Un administrador no puede quitarse a si mismo el rol Admin,
    /// ni dejar al sistema sin ningun administrador.
    /// </summary>
    Task<Result<AdminUserDto>> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);
}
