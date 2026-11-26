using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;
using WalletLedger.Application.Pix;

namespace WalletLedger.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/v1/pix/charges")]
[Produces("application/json")]
public sealed class ChargesController : ControllerBase
{
    /// <summary>
    /// Gera o texto de um QR code de cobrança no padrão BR Code (o mesmo dos bancos)
    /// para uma chave da própria conta. O app transforma o texto em imagem.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ChargeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ChargeResponse> Create(CreateChargeRequest request, [FromServices] CreateChargeHandler handler, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId() ?? throw new InvalidOperationException("Cliente sem conta no token.");
        return handler.HandleAsync(accountId, request, cancellationToken);
    }
}
