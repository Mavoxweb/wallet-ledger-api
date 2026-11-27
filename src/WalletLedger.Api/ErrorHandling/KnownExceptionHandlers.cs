using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletLedger.Domain.Common;

namespace WalletLedger.Api.ErrorHandling;

/// <summary>
/// Regra de negócio violada vira ProblemDetails com o <c>code</c> estável. A maioria é
/// 422 (pedido entendido, mas não pode ser feito); alguns códigos têm status próprio.
/// </summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    private static readonly Dictionary<string, int> StatusByCode = new()
    {
        ["invalid_credentials"] = StatusCodes.Status401Unauthorized,
        ["invalid_refresh_token"] = StatusCodes.Status401Unauthorized,
        ["email_taken"] = StatusCodes.Status409Conflict,
        ["cpf_taken"] = StatusCodes.Status409Conflict,
        ["pix_key_taken"] = StatusCodes.Status409Conflict,
        ["account_already_blocked"] = StatusCodes.Status409Conflict,
        ["account_not_blocked"] = StatusCodes.Status409Conflict,
        ["transaction_already_reversed"] = StatusCodes.Status409Conflict,
        ["not_found"] = StatusCodes.Status404NotFound,
    };

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var status = StatusByCode.GetValueOrDefault(domain.Code, StatusCodes.Status422UnprocessableEntity);
        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails { Status = status, Title = domain.Message };
        problem.Extensions["code"] = domain.Code;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }
}

/// <summary>Erros de validação de entrada (FluentValidation) viram 400 com a lista por campo.</summary>
internal sealed class ValidationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validation)
        {
            return false;
        }

        var errors = validation.Errors
            .GroupBy(e => JsonName(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dados inválidos",
            },
        });
    }

    private static string JsonName(string property) =>
        string.IsNullOrEmpty(property) ? property : char.ToLowerInvariant(property[0]) + property[1..];
}

/// <summary>
/// Violação de índice único que escapou das checagens antecipadas (dois pedidos
/// iguais ao mesmo tempo): é conflito (409), não erro interno.
/// </summary>
internal sealed class UniqueViolationHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Registro já existe." };
        problem.Extensions["code"] = "conflict";

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }
}
