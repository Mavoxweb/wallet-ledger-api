using System.Security.Claims;
using WalletLedger.Infrastructure.Auth;

namespace WalletLedger.Api.Auth;

internal static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token sem 'sub'."));

    /// <summary>Conta do cliente logado. Operadores e admins não têm conta.</summary>
    public static Guid? GetAccountId(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtTokenService.AccountIdClaim) is { } value ? Guid.Parse(value) : null;
}
