using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;
using WalletLedger.Api.Filters;
using WalletLedger.Application.Backoffice;

namespace WalletLedger.Api.Controllers;

/// <summary>
/// Ações sobre transações do razão. Só o perfil Admin: o operador consulta e
/// bloqueia, mas não move dinheiro.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/backoffice/transactions")]
[Produces("application/json")]
public sealed class BackofficeTransactionsController : ControllerBase
{
    /// <summary>
    /// Estorna a transação: cria uma transação nova com os lançamentos opostos e marca
    /// a original como estornada. Quem devolve o dinheiro precisa ter saldo; vale
    /// mesmo com a conta bloqueada. Exige o header Idempotency-Key e um motivo.
    /// </summary>
    [HttpPost("{id:guid}/reversal")]
    [RequiresIdempotencyKey]
    [ProducesResponseType<ReversalResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reverse(
        Guid id,
        ReasonRequest request,
        [FromServices] ReverseTransactionHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            User.GetUserId(), id, RequiresIdempotencyKeyAttribute.From(HttpContext), request, cancellationToken);

        return new ContentResult { StatusCode = result.StatusCode, Content = result.JsonBody, ContentType = "application/json" };
    }
}
