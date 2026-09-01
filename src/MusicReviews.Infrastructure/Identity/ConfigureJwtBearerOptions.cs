using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MusicReviews.Infrastructure.Identity;

/// <summary>
/// Configura el esquema Bearer a partir de <see cref="JwtOptions"/> ya enlazadas.
/// </summary>
/// <remarks>
/// La alternativa obvia seria leer la configuracion dentro de <c>AddJwtBearer(...)</c>
/// con <c>configuration.GetSection("Jwt").Get&lt;JwtOptions&gt;()</c>. No hacerlo es
/// deliberado: esa lectura ocurre al REGISTRAR los servicios, mientras que
/// <see cref="TokenService"/> recibe las opciones por <see cref="IOptions{T}"/> y las
/// resuelve mas tarde. Si alguna fuente de configuracion se agrega despues del registro
/// —es exactamente lo que hace <c>WebApplicationFactory</c> en los tests de
/// integracion— las dos lecturas divergen: se firma con una clave y se valida con otra,
/// y todo request autenticado responde 401 con IDX10503.
///
/// Tomando las opciones por inyeccion, emisor y validador leen siempre la misma
/// instancia enlazada.
/// </remarks>
internal sealed class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly JwtOptions _jwt;

    public ConfigureJwtBearerOptions(IOptions<JwtOptions> jwt)
    {
        _jwt = jwt.Value;
    }

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        // Sin el mapeo legacy: 'sub' sigue llamandose 'sub' y no se convierte
        // en el ClaimType largo de WS-Federation.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = _jwt.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),

            ValidateLifetime = true,
            // Por defecto son 5 minutos de tolerancia: un access token de 15 minutos
            // seguiria siendo aceptado 20. Con tokens cortos eso no sirve.
            ClockSkew = TimeSpan.Zero,

            NameClaimType = AuthClaimTypes.Name,
            RoleClaimType = AuthClaimTypes.Role
        };
    }
}
