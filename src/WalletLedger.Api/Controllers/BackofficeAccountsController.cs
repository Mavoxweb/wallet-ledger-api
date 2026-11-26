using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Application.Backoffice;
using WalletLedger.Domain.Accounts;

namespace WalletLedger.Api.Controllers;

/// <summary>
/// Contas de cliente vistas pela equipe de operação. Clientes recebem 403: a
/// regra está aqui, no servidor, e não só na tela do backoffice.
/// </summary>
[ApiController]
[Authorize(Roles = "Operator,Admin")]
[Route("api/v1/backoffice/accounts")]
[Produces("application/json")]
public sealed class BackofficeAccountsController : ControllerBase
{
    /// <summary>
    /// Busca contas por CPF (com ou sem pontuação), e-mail, final da conta ou parte do
    /// nome. Sem <c>q</c>, lista todas, da mais nova para a mais antiga.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<BackofficeAccountPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<BackofficeAccountPage> Search(
        [FromServices] SearchAccountsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? q = null,
        [FromQuery] AccountStatus? status = null,
        [FromQuery] int limit = SearchAccountsHandler.DefaultLimit,
        [FromQuery] string? cursor = null) =>
        handler.HandleAsync(q, status, limit, cursor, cancellationToken);

    /// <summary>Titular (CPF mascarado), status, saldo e chaves Pix.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<BackofficeAccountDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BackofficeAccountDetail> Get(Guid id, [FromServices] GetAccountDetailHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(id, cancellationToken);
}
