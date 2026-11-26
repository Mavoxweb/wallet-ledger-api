using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletLedger.Api.Auth;
using WalletLedger.Application.Pix;
using WalletLedger.Domain.Pix;

namespace WalletLedger.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/v1/pix/keys")]
[Produces("application/json")]
public sealed class PixKeysController : ControllerBase
{
    private Guid AccountId => User.GetAccountId() ?? throw new InvalidOperationException("Cliente sem conta no token.");

    /// <summary>Chaves PIX da própria conta.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PixKeyResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PixKeyResponse>> List([FromServices] ListPixKeysHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(AccountId, cancellationToken);

    /// <summary>Cadastra uma chave (até 5 por conta). Para "evp", o valor é gerado pelo sistema.</summary>
    [HttpPost]
    [ProducesResponseType<PixKeyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(CreatePixKeyRequest request, [FromServices] CreatePixKeyHandler handler, CancellationToken cancellationToken)
    {
        var key = await handler.HandleAsync(AccountId, request, cancellationToken);
        return Created($"/api/v1/pix/keys/{key.Id}", key);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] DeletePixKeyHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(AccountId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Consulta o dono de uma chave antes de transferir: devolve só nome e CPF mascarados.</summary>
    [HttpGet("lookup")]
    [ProducesResponseType<PixKeyLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PixKeyLookupResponse> Lookup(
        [FromQuery] PixKeyType type,
        [FromQuery] string value,
        [FromServices] LookupPixKeyHandler handler,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(type, value, cancellationToken);
}
