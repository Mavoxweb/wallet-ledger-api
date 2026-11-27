using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Application.Backoffice;
using WalletLedger.Domain.Audit;

namespace WalletLedger.Api.Controllers;

[ApiController]
[Authorize(Roles = "Operator,Admin")]
[Route("api/v1/backoffice/audit")]
[Produces("application/json")]
public sealed class BackofficeAuditController : ControllerBase
{
    /// <summary>
    /// Trilha de auditoria, da ação mais recente para a mais antiga. Filtros
    /// opcionais por conta, por quem fez e por tipo de ação.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<AuditPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<AuditPage> List(
        [FromServices] GetAuditHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? accountId = null,
        [FromQuery] Guid? actorId = null,
        [FromQuery] AuditAction? action = null,
        [FromQuery] int limit = GetAuditHandler.DefaultLimit,
        [FromQuery] string? cursor = null) =>
        handler.HandleAsync(accountId, actorId, action, limit, cursor, cancellationToken);
}
