using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using WalletLedger.Api.Filters;

namespace WalletLedger.Api.OpenApi;

/// <summary>Ajustes no contrato OpenAPI gerado a partir dos controllers.</summary>
internal static class OpenApiSetup
{
    private const string BearerScheme = "Bearer";

    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Wallet Ledger API",
                Version = "v1",
                Description =
                    "Carteira digital com livro-razão de partidas dobradas: contas, depósitos, " +
                    "transferências PIX por chave, extrato e QR code BR Code. " +
                    "Valores sempre em centavos (amountCents). Erros no formato ProblemDetails (RFC 9457).",
            };

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token obtido em POST /api/v1/auth/login.",
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            // Comentários /// de várias linhas carregam a quebra de linha do sistema (\r\n no
            // Windows, \n no Linux). Padronizando, o contrato sai igual em qualquer máquina.
            operation.Summary = operation.Summary?.ReplaceLineEndings("\n");
            operation.Description = operation.Description?.ReplaceLineEndings("\n");

            var metadata = context.Description.ActionDescriptor.EndpointMetadata;

            if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
                });
            }

            if (metadata.OfType<RequiresIdempotencyKeyAttribute>().Any())
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = RequiresIdempotencyKeyAttribute.HeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "Identificador único da operação (ex.: um UUID). Repetir a mesma chave devolve a mesma resposta sem executar de novo.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 100, Pattern = "^[A-Za-z0-9_-]+$" },
                });
            }

            return Task.CompletedTask;
        });
    }
}
