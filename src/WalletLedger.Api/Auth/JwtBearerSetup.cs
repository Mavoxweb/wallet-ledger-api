using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WalletLedger.Infrastructure.Auth;

namespace WalletLedger.Api.Auth;

/// <summary>
/// Configura a validação do JWT com as mesmas configurações que emitem o token, e
/// usando o <see cref="TimeProvider"/> injetado para checar a expiração. Assim o
/// relógio dos testes também vale aqui.
/// </summary>
internal sealed class JwtBearerSetup(IOptions<JwtSettings> settings, TimeProvider clock)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        var jwt = settings.Value;

        options.MapInboundClaims = false;
        options.TimeProvider = clock;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = JwtTokenService.SigningKey(jwt),
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (_, expires, _, _) => expires is not null && expires.Value > clock.GetUtcNow().UtcDateTime,
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
