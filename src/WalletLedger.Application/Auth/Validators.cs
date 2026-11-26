using FluentValidation;
using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(120);

        RuleFor(r => r.Cpf)
            .Must(cpf => Cpf.TryParse(cpf, out _))
            .WithMessage("CPF inválido.");

        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);

        RuleFor(r => r.Password)
            .MinimumLength(8).WithMessage("A senha precisa ter pelo menos 8 caracteres.")
            .Must(p => p is not null && p.Any(char.IsLetter)).WithMessage("A senha precisa ter pelo menos uma letra.")
            .Must(p => p is not null && p.Any(char.IsDigit)).WithMessage("A senha precisa ter pelo menos um número.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty();
        RuleFor(r => r.Password).NotEmpty();
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(r => r.RefreshToken).NotEmpty().MaximumLength(200);
}
