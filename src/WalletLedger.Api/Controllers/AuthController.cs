using Microsoft.AspNetCore.Mvc;
using WalletLedger.Application.Auth;

namespace WalletLedger.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>Cadastra um cliente e abre a conta dele.</summary>
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, [FromServices] RegisterHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return Created("/api/v1/me", result);
    }

    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<TokenResponse> Login(LoginRequest request, [FromServices] LoginHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(request, cancellationToken);

    /// <summary>Troca o refresh token por um par novo. O token usado deixa de valer.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<TokenResponse> Refresh(RefreshRequest request, [FromServices] RefreshHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(request, cancellationToken);

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, [FromServices] LogoutHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return NoContent();
    }
}
