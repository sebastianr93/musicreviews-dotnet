using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Admin;
using MusicReviews.Application.Admin.Dtos;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Domain.Constants;
using MusicReviews.Domain.Entities;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Admin;

/// <inheritdoc cref="IAdminService"/>
internal sealed class AdminService : IAdminService
{
    private const int MostReviewedAlbumsLimit = 10;

    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<AdminService> logger)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Estadisticas
    // ------------------------------------------------------------------

    /// <remarks>
    /// Son varios COUNT separados en vez de una sentencia con subconsultas. Es a
    /// proposito: cada uno es un conteo indexado que Postgres resuelve rapido, y
    /// meterlos todos en una sola query los volveria ilegibles sin ganar nada
    /// medible a esta escala. Si el panel creciera, el paso siguiente no es
    /// optimizar la query sino cachear el resultado unos minutos: son datos
    /// agregados que nadie necesita al segundo.
    /// </remarks>
    public async Task<Result<AdminStatsDto>> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var since = _timeProvider.GetUtcNow().AddDays(-30);

        var totalUsers = await _context.Users.CountAsync(cancellationToken);
        var newUsers = await _context.Users.CountAsync(u => u.CreatedAt >= since, cancellationToken);

        // "Activo" = escribio algo en los ultimos 30 dias. Union de autores de
        // reviews y de comentarios, sin duplicar a quien hizo las dos cosas.
        var activeUsers = await _context.Reviews
            .Where(r => r.CreatedAt >= since)
            .Select(r => r.UserId)
            .Union(_context.Comments
                .Where(c => c.CreatedAt >= since && !c.IsDeleted)
                .Select(c => c.UserId))
            .CountAsync(cancellationToken);

        var totalReviews = await _context.Reviews.CountAsync(cancellationToken);
        var totalComments = await _context.Comments.CountAsync(c => !c.IsDeleted, cancellationToken);
        var totalVotes = await _context.Likes.CountAsync(cancellationToken);
        var cachedArtists = await _context.Artists.CountAsync(cancellationToken);
        var cachedAlbums = await _context.Albums.CountAsync(cancellationToken);

        var averageScore = await _context.Reviews
            .Select(r => (double?)r.Score)
            .AverageAsync(cancellationToken);

        var mostReviewed = await _context.Albums
            .Where(a => a.Reviews.Any())
            .OrderByDescending(a => a.Reviews.Count())
            .ThenBy(a => a.Id)
            .Take(MostReviewedAlbumsLimit)
            .Select(a => new MostReviewedAlbumDto(
                a.Id,
                a.MusicBrainzId,
                a.Title,
                a.Artist.Name,
                a.CoverArtUrl,
                a.Reviews.Count(),
                a.Reviews.Average(r => r.Score)))
            .ToListAsync(cancellationToken);

        return Result.Success(new AdminStatsDto(
            totalUsers,
            newUsers,
            activeUsers,
            totalReviews,
            totalComments,
            totalVotes,
            cachedArtists,
            cachedAlbums,
            averageScore,
            mostReviewed));
    }

    // ------------------------------------------------------------------
    // Usuarios
    // ------------------------------------------------------------------

    public async Task<Result<PagedResult<AdminUserDto>>> GetUsersAsync(
        PageRequest page,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();

            // Se filtra por las columnas normalizadas de Identity: estan indexadas
            // y ya guardan el valor en mayusculas, asi que no hace falta un ILIKE
            // sobre la columna original.
            query = query.Where(u =>
                (u.NormalizedUserName != null && u.NormalizedUserName.Contains(term)) ||
                (u.NormalizedEmail != null && u.NormalizedEmail.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);

        if (total == 0)
        {
            return Result.Success(PagedResult<AdminUserDto>.Empty(page.Page, page.PageSize));
        }

        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .Select(ProjectUser())
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<AdminUserDto>(items, page.Page, page.PageSize, total));
    }

    public async Task<Result<AdminUserDto>> AssignRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = request.Role.Trim();

        if (!AppRoles.All.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<AdminUserDto>(Error.Validation(
                "admin.unknown_role",
                $"El rol '{role}' no existe. Roles validos: {string.Join(", ", AppRoles.All)}."));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return Result.Failure<AdminUserDto>(UserNotFound());
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            await _roleManager.CreateAsync(new ApplicationRole(role));
        }

        if (!await _userManager.IsInRoleAsync(user, role))
        {
            var result = await _userManager.AddToRoleAsync(user, role);

            if (!result.Succeeded)
            {
                return Result.Failure<AdminUserDto>(Error.Failure(
                    "admin.assign_role_failed",
                    string.Join(" ", result.Errors.Select(e => e.Description))));
            }

            _logger.LogInformation(
                "El administrador {AdminId} asigno el rol {Role} a {UserId}",
                _currentUser.UserId, role, userId);
        }

        return await GetUserAsync(userId, cancellationToken);
    }

    public async Task<Result<AdminUserDto>> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        role = role.Trim();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return Result.Failure<AdminUserDto>(UserNotFound());
        }

        // Quitarse a uno mismo el rol Admin deja la sesion sin permisos en el acto
        // y es la forma mas facil de perder el acceso al panel por accidente.
        if (role.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase)
            && _currentUser.UserId == userId)
        {
            return Result.Failure<AdminUserDto>(Error.Validation(
                "admin.cannot_demote_self",
                "No podés quitarte a vos mismo el rol de administrador."));
        }

        if (role.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            // Si este es el ultimo administrador, nadie podria volver a asignar el rol:
            // habria que tocar la base a mano para recuperar el acceso.
            var admins = await _userManager.GetUsersInRoleAsync(AppRoles.Admin);

            if (admins.Count <= 1)
            {
                return Result.Failure<AdminUserDto>(Error.Validation(
                    "admin.last_admin",
                    "No se puede quitar el último administrador del sistema."));
            }
        }

        if (await _userManager.IsInRoleAsync(user, role))
        {
            var result = await _userManager.RemoveFromRoleAsync(user, role);

            if (!result.Succeeded)
            {
                return Result.Failure<AdminUserDto>(Error.Failure(
                    "admin.remove_role_failed",
                    string.Join(" ", result.Errors.Select(e => e.Description))));
            }

            _logger.LogInformation(
                "El administrador {AdminId} quito el rol {Role} a {UserId}",
                _currentUser.UserId, role, userId);
        }

        return await GetUserAsync(userId, cancellationToken);
    }

    private async Task<Result<AdminUserDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(ProjectUser())
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? Result.Failure<AdminUserDto>(UserNotFound())
            : Result.Success(user);
    }

    /// <summary>
    /// Proyeccion con los roles resueltos como subconsulta de coleccion. EF la
    /// materializa en una segunda sentencia, no una por usuario: el N+1 seria
    /// consultar los roles dentro de un foreach sobre la lista ya cargada.
    /// </summary>
    private System.Linq.Expressions.Expression<Func<ApplicationUser, AdminUserDto>> ProjectUser()
    {
        var userRoles = _context.UserRoles;
        var roles = _context.Roles;
        var now = _timeProvider.GetUtcNow();

        return user => new AdminUserDto(
            user.Id,
            user.UserName!,
            user.Email!,
            user.CreatedAt,
            userRoles
                .Where(ur => ur.UserId == user.Id)
                .Join(roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToList(),
            user.Reviews.Count(),
            user.Comments.Count(c => !c.IsDeleted),
            user.LockoutEnd != null && user.LockoutEnd > now);
    }

    private static Error UserNotFound() =>
        Error.NotFound("users.not_found", "No existe ese usuario.");
}
