using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Auth;

/// <summary>Cadastra um cliente e abre a conta dele, tudo na mesma transação.</summary>
public sealed class RegisterHandler(
    IWalletDbContext db,
    IPasswordHasher passwordHasher,
    IValidator<RegisterRequest> validator,
    TimeProvider clock)
{
    public async Task<RegisterResponse> HandleAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        // O validador já garantiu que o CPF é válido; aqui só convertemos.
        if (!Cpf.TryParse(request.Cpf, out var cpf))
        {
            throw new DomainException("invalid_cpf", "CPF inválido.");
        }

        var email = User.NormalizeEmail(request.Email);

        // Checagem antecipada para dar um erro claro. O índice único no banco continua
        // sendo a garantia final caso dois cadastros iguais cheguem ao mesmo tempo.
        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new DomainException("email_taken", "Este e-mail já está cadastrado.");
        }

        if (await db.Users.AnyAsync(u => u.Cpf == cpf, cancellationToken))
        {
            throw new DomainException("cpf_taken", "Este CPF já está cadastrado.");
        }

        var now = clock.GetUtcNow();
        var user = User.Create(request.Name, cpf!, email, passwordHasher.Hash(request.Password), Role.Customer, now);
        var account = Account.OpenForCustomer(user.Id, now);

        db.Users.Add(user);
        db.Accounts.Add(account);

        await db.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(user.Id, account.Id);
    }
}
