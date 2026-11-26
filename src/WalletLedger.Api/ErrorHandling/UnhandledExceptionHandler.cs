using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WalletLedger.Api.ErrorHandling;

/// <summary>
/// Última linha de defesa: qualquer exceção não tratada vira um 500 no formato
/// ProblemDetails. Fora de Development, nenhum detalhe interno vaza na resposta.
/// </summary>
internal sealed partial class UnhandledExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandled(logger, exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno",
            Detail = environment.IsDevelopment() ? exception.ToString() : null,
        };

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
