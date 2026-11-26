using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Application.Common;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Pix;

namespace WalletLedger.Application.Pix;

public sealed record CreateChargeRequest(Guid KeyId, long? AmountCents);

public sealed record ChargeResponse(string Payload);

public sealed class CreateChargeRequestValidator : AbstractValidator<CreateChargeRequest>
{
    public CreateChargeRequestValidator()
    {
        RuleFor(r => r.KeyId).NotEmpty();
        RuleFor(r => r.AmountCents!.Value).InclusiveBetween(1, Limits.MaxAmountCents).When(r => r.AmountCents is not null)
            .OverridePropertyName("amountCents");
    }
}

/// <summary>
/// Gera o payload de um QR code de cobrança (BR Code estático) para uma chave da
/// própria conta. Com valor, o pagador não consegue alterá-lo; sem valor, ele digita.
/// </summary>
public sealed class CreateChargeHandler(IWalletDbContext db, IValidator<CreateChargeRequest> validator)
{
    // O cadastro não pede cidade; o BR Code exige uma, então usamos um valor fixo.
    public const string MerchantCity = "SAO PAULO";

    public async Task<ChargeResponse> HandleAsync(Guid accountId, CreateChargeRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var data = await (
            from key in db.PixKeys
            join account in db.Accounts on key.AccountId equals account.Id
            join user in db.Users on account.OwnerUserId equals user.Id
            where key.Id == request.KeyId && key.AccountId == accountId
            select new { key.Value, user.Name })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("not_found", "Chave PIX não encontrada.");

        return new ChargeResponse(BrCode.BuildStatic(data.Value, data.Name, MerchantCity, request.AmountCents, txid: null));
    }
}
