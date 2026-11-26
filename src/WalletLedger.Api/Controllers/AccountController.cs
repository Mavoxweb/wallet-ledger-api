using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;
using WalletLedger.Application.Statements;

namespace WalletLedger.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/v1/account")]
[Produces("application/json")]
public sealed class AccountController : ControllerBase
{
    private Guid AccountId => User.GetAccountId() ?? throw new InvalidOperationException("Cliente sem conta no token.");

    /// <summary>Saldo e status da própria conta.</summary>
    [HttpGet]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public Task<AccountResponse> Get([FromServices] GetBalanceHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(AccountId, cancellationToken);

    /// <summary>
    /// Extrato, do mais novo para o mais antigo. Para a próxima página, envie o
    /// <c>nextCursor</c> recebido; quando ele vier nulo, acabou.
    /// </summary>
    [HttpGet("statement")]
    [ProducesResponseType<StatementPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<StatementPage> Statement(
        [FromServices] GetStatementHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int limit = GetStatementHandler.DefaultLimit,
        [FromQuery] string? cursor = null) =>
        handler.HandleAsync(AccountId, limit, cursor, cancellationToken);
}
