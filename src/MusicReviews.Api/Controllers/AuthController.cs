using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Auth;
using MusicReviews.Application.Auth.Dtos;
using MusicReviews.Application.Users.Dtos;
using MusicReviews.Application.Common.Interfaces;

namespace MusicReviews.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUser _currentUser;

    public AuthController(IAuthService authService, ICurrentUser currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>Crea una cuenta nueva y devuelve el par de tokens.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, GetIpAddress(), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.Problem(result.Error);
    }

    /// <summary>Autentica al usuario y devuelve el par de tokens.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, GetIpAddress(), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.Problem(result.Error);
    }

    /// <summary>Rota el refresh token y devuelve un par nuevo.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshAsync(request, GetIpAddress(), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.Problem(result.Error);
    }

    /// <summary>Revoca un refresh token. Idempotente.</summary>
    /// <summary>
    /// Cambia la contraseña del usuario autenticado. Revoca todas sus sesiones, incluida
    /// la que hizo el cambio: hay que volver a entrar.
    /// </summary>
    [HttpPost("password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ChangePasswordAsync(request, GetIpAddress(), cancellationToken);

        return result.IsSuccess ? NoContent() : this.Problem(result.Error);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RevokeTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RevokeAsync(request, GetIpAddress(), cancellationToken);

        return result.IsSuccess
            ? NoContent()
            : this.Problem(result.Error);
    }

    /// <summary>Devuelve la identidad asociada al access token de la request. Util para probar el bearer.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId = _currentUser.UserId,
            userName = _currentUser.UserName,
            roles = User.Claims
                .Where(c => c.Type == "role")
                .Select(c => c.Value)
                .ToArray()
        });
    }

    /// <summary>
    /// IP del cliente. Se prioriza X-Forwarded-For porque en produccion la Api
    /// va detras de un reverse proxy y RemoteIpAddress seria la del proxy.
    /// </summary>
    private string? GetIpAddress()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString().Split(',', StringSplitOptions.TrimEntries).FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
