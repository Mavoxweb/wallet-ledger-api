using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;
using WalletLedger.Application.Backoffice;
using WalletLedger.Application.Statements;
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

    /// <summary>
    /// Extrato da conta, do mais novo para o mais antigo, com o status de cada
    /// transação e se ela ainda pode ser estornada.
    /// </summary>
    [HttpGet("{id:guid}/statement")]
    [ProducesResponseType<BackofficeStatementPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BackofficeStatementPage> Statement(
        Guid id,
        [FromServices] BackofficeStatementHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int limit = GetStatementHandler.DefaultLimit,
        [FromQuery] string? cursor = null) =>
        handler.HandleAsync(id, limit, cursor, cancellationToken);

    /// <summary>
    /// Bloqueia a conta: ela para de enviar e receber, inclusive em sessões já
    /// abertas no app. O motivo (10 a 500 caracteres) vai para a auditoria.
    /// </summary>
    [HttpPost("{id:guid}/block")]
    [ProducesResponseType<BackofficeAccountDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<BackofficeAccountDetail> Block(
        Guid id,
        ReasonRequest request,
        [FromServices] BlockAccountHandler handler,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(User.GetUserId(), id, request, cancellationToken);

    /// <summary>Desbloqueia a conta. O motivo vai para a auditoria.</summary>
    [HttpPost("{id:guid}/unblock")]
    [ProducesResponseType<BackofficeAccountDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<BackofficeAccountDetail> Unblock(
        Guid id,
        ReasonRequest request,
        [FromServices] UnblockAccountHandler handler,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(User.GetUserId(), id, request, cancellationToken);
}
