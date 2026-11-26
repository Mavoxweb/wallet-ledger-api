namespace WalletLedger.Application.Auth;

public sealed record RegisterRequest(string Name, string Cpf, string Email, string Password);

public sealed record RegisterResponse(Guid UserId, Guid AccountId);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
