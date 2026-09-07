using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Auth;
using MusicReviews.Application.Auth.Dtos;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Users.Dtos;
using MusicReviews.Domain.Constants;
using MusicReviews.Domain.Entities;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Identity;

/// <inheritdoc cref="IAuthService"/>
public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        ITokenService tokenService,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _context = context;
        _tokenService = tokenService;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        var userName = request.UserName.Trim();

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return Result.Failure<AuthResponse>(
                Error.Conflict("auth.email_taken", "Ya existe una cuenta con ese email."));
        }

        if (await _userManager.FindByNameAsync(userName) is not null)
        {
            return Result.Failure<AuthResponse>(
                Error.Conflict("auth.username_taken", "Ese nombre de usuario ya está en uso."));
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            Email = email,
            CreatedAt = _timeProvider.GetUtcNow()
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);

        if (!createResult.Succeeded)
        {
            var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
            return Result.Failure<AuthResponse>(Error.Validation("auth.register_failed", message));
        }

        await _userManager.AddToRoleAsync(user, AppRoles.User);

        _logger.LogInformation("Usuario registrado: {UserId} ({UserName})", user.Id, user.UserName);

        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    public async Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var identifier = request.UserNameOrEmail.Trim();

        var user = identifier.Contains('@')
            ? await _userManager.FindByEmailAsync(identifier)
            : await _userManager.FindByNameAsync(identifier);

        // Mismo mensaje para "usuario inexistente" y "contraseña incorrecta":
        // discriminar permite enumerar cuentas validas.
        var invalidCredentials = Error.Unauthorized(
            "auth.invalid_credentials",
            "Usuario o contraseña incorrectos.");

        if (user is null)
        {
            return Result.Failure<AuthResponse>(invalidCredentials);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return Result.Failure<AuthResponse>(Error.Forbidden(
                "auth.locked_out",
                "La cuenta está bloqueada temporalmente por intentos fallidos. Volvé a probar en unos minutos."));
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            // Alimenta el contador de bloqueo por fuerza bruta.
            await _userManager.AccessFailedAsync(user);
            return Result.Failure<AuthResponse>(invalidCredentials);
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    public async Task<Result<AuthResponse>> RefreshAsync(
        RefreshTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);

        var stored = await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        var invalidToken = Error.Unauthorized(
            "auth.invalid_refresh_token",
            "El refresh token no es válido.");

        if (stored is null)
        {
            return Result.Failure<AuthResponse>(invalidToken);
        }

        // Reuso de un token ya revocado: alguien tiene una copia vieja. Se corta la
        // familia entera para forzar un login nuevo.
        if (stored.IsRevoked)
        {
            _logger.LogWarning(
                "Reuso de refresh token revocado para el usuario {UserId}. Se revocan todos sus tokens activos.",
                stored.UserId);

            await RevokeAllActiveTokensAsync(stored.UserId, ipAddress, now, cancellationToken);

            return Result.Failure<AuthResponse>(invalidToken);
        }

        if (stored.IsExpired(now))
        {
            return Result.Failure<AuthResponse>(Error.Unauthorized(
                "auth.refresh_token_expired",
                "El refresh token venció. Iniciá sesión de nuevo."));
        }

        var replacement = _tokenService.CreateRefreshToken();

        stored.RevokedAt = now;
        stored.RevokedByIp = ipAddress;
        stored.ReplacedByTokenHash = replacement.Hash;

        return await IssueTokensAsync(stored.User, ipAddress, cancellationToken, replacement);
    }

    public async Task<Result> RevokeAsync(
        RevokeTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);

        var stored = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || !stored.IsActive(now))
        {
            // Idempotente: revocar algo ya revocado o inexistente no es un error para el cliente.
            return Result.Success();
        }

        stored.RevokedAt = now;
        stored.RevokedByIp = ipAddress;

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Emite el par de tokens y persiste el refresh token (hasheado) en una sola transaccion
    /// junto con la revocacion del token anterior, si la hubo.
    /// </summary>
    private async Task<Result<AuthResponse>> IssueTokensAsync(
        ApplicationUser user,
        string? ipAddress,
        CancellationToken cancellationToken,
        GeneratedRefreshToken? refreshToken = null)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);

        refreshToken ??= _tokenService.CreateRefreshToken();

        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshToken.Hash,
            ExpiresAt = refreshToken.ExpiresAt,
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedByIp = ipAddress
        });

        await _context.SaveChangesAsync(cancellationToken);

        var response = new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt,
            new UserSummaryDto(
                user.Id,
                user.UserName ?? string.Empty,
                user.Email ?? string.Empty,
                user.AvatarUrl,
                [.. roles]));

        return Result.Success(response);
    }

    public async Task<Result> ChangePasswordAsync(
        ChangePasswordRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Tenés que iniciar sesión."));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return Result.Failure(Error.NotFound("users.not_found", "No existe ese usuario."));
        }

        var result = await _userManager.ChangePasswordAsync(
            user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            // El mensaje no distingue entre "la actual es incorrecta" y "la nueva no
            // cumple las reglas": Identity ya devuelve el detalle y repetirlo aca solo
            // agrega una forma de contarle a alguien si acerto la contraseña.
            var detalle = string.Join(" ", result.Errors.Select(e => e.Description));

            _logger.LogInformation("Cambio de contraseña rechazado para {UserId}", userId);

            return Result.Failure(Error.Validation(
                "auth.password_change_failed",
                string.IsNullOrWhiteSpace(detalle)
                    ? "No se pudo cambiar la contraseña."
                    : detalle));
        }

        // Se revocan TODAS las sesiones, incluida la que hizo el cambio. Cambiar la
        // contraseña se hace, casi siempre, porque se sospecha que alguien mas entro:
        // si los refresh tokens viejos siguieran renovandose, el cambio no serviria
        // para nada.
        await RevokeAllActiveTokensAsync(userId, ipAddress, _timeProvider.GetUtcNow(), cancellationToken);

        _logger.LogInformation("Contraseña cambiada por {UserId}; se revocaron sus sesiones", userId);

        return Result.Success();
    }

    private async Task RevokeAllActiveTokensAsync(
        Guid userId,
        string? ipAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdate: una sola sentencia UPDATE, sin materializar las entidades.
        await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedByIp, ipAddress),
                cancellationToken);
    }
}
