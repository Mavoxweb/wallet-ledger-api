using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;
using WalletLedger.Api.Filters;
using WalletLedger.Application.Deposits;

namespace WalletLedger.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/v1/deposits")]
[Produces("application/json")]
public sealed class DepositsController : ControllerBase
{
    /// <summary>
    /// Depósito de demonstração na própria conta (até R$ 1.000,00 por vez e
    /// R$ 5.000,00 por dia). Exige o header Idempotency-Key.
    /// </summary>
    [HttpPost("simulated")]
    [RequiresIdempotencyKey]
    [ProducesResponseType<SimulatedDepositResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Simulated(
        SimulatedDepositRequest request,
        [FromServices] SimulatedDepositHandler handler,
        CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId() ?? throw new InvalidOperationException("Cliente sem conta no token.");

        var result = await handler.HandleAsync(
            User.GetUserId(), accountId, RequiresIdempotencyKeyAttribute.From(HttpContext), request, cancellationToken);

        // A resposta já vem serializada: numa repetição, os bytes são exatamente os mesmos.
        return new ContentResult { StatusCode = result.StatusCode, Content = result.JsonBody, ContentType = "application/json" };
    }
}
