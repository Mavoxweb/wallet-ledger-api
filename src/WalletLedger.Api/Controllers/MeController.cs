using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;

namespace WalletLedger.Api.Controllers;

public sealed record MeResponse(Guid UserId, string Name, string Role, Guid? AccountId);

[ApiController]
[Authorize]
[Route("api/v1/me")]
[Produces("application/json")]
public sealed class MeController : ControllerBase
{
    /// <summary>Perfil do usuário logado, lido do próprio token.</summary>
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public MeResponse Get() => new(
        User.GetUserId(),
        User.FindFirst("name")?.Value ?? string.Empty,
        User.FindFirst("role")?.Value ?? string.Empty,
        User.GetAccountId());
}
