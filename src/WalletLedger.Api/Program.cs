using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Scalar.AspNetCore;
using Serilog;
using WalletLedger.Api.Auth;
using WalletLedger.Api.ErrorHandling;
using WalletLedger.Api.Filters;
using WalletLedger.Api.OpenApi;
using WalletLedger.Application;
using WalletLedger.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// No build, a ferramenta "GetDocument.Insider" carrega a aplicação só para extrair o
// contrato OpenAPI. Nesse caso não há banco nem segredos configurados.
var isGeneratingOpenApi = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, runStartupTasks: !isGeneratingOpenApi);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.ConfigureOptions<JwtBearerSetup>();
builder.Services.AddAuthorization();

// Enums trafegam como texto em camelCase ("email", "transfer"). A segunda configuração
// é a que o gerador do contrato OpenAPI lê; as duas precisam concordar.
// Números só como número: "amountCents": "100" é recusado, e o contrato tipa os
// campos como inteiro puro (e não "inteiro ou texto"), o que simplifica os clientes gerados.
builder.Services.AddControllers()
    .AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
builder.Services.AddOpenApi(options => OpenApiSetup.Configure(options));
builder.Services.AddProblemDetails();

// A ordem importa: os tratadores específicos vêm antes do genérico (500).
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<UniqueViolationHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "database");

// CORS: o app Flutter na web roda em outra origem. Só as origens da configuração
// (Cors:AllowedOrigins) podem chamar, e só com os cabeçalhos que a API usa.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "DELETE")
    .WithHeaders("Authorization", "Content-Type", RequiresIdempotencyKeyAttribute.HeaderName)));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

// Contrato em /openapi/v1.json e documentação interativa em /docs (desenvolvimento e demo).
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demo"))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/docs", options => options
        .WithTitle("Wallet Ledger API")
        .AddPreferredSecuritySchemes("Bearer"));
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

await app.RunAsync();

public partial class Program
{
    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
