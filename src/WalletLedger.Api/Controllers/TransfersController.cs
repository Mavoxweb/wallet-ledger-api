using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;
using WalletLedger.Api.Filters;
using WalletLedger.Application.Transfers;

namespace WalletLedger.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/v1/transfers")]
[Produces("application/json")]
public sealed class TransfersController : ControllerBase
{
    /// <summary>Transferência PIX por chave. Exige o header Idempotency-Key.</summary>
    [HttpPost("pix")]
    [RequiresIdempotencyKey]
    [ProducesResponseType<PixTransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Pix(
        PixTransferRequest request,
        [FromServices] TransferByPixKeyHandler handler,
        CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId() ?? throw new InvalidOperationException("Cliente sem conta no token.");

        var result = await handler.HandleAsync(
            User.GetUserId(), accountId, RequiresIdempotencyKeyAttribute.From(HttpContext), request, cancellationToken);

        return new ContentResult { StatusCode = result.StatusCode, Content = result.JsonBody, ContentType = "application/json" };
    }
}
