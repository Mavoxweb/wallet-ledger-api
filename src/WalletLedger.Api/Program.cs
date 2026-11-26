using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Serilog;
using WalletLedger.Api.Auth;
using WalletLedger.Api.ErrorHandling;
using WalletLedger.Application;
using WalletLedger.Application.Abstractions;
using WalletLedger.Infrastructure;
using WalletLedger.Infrastructure.Persistence;
using WalletLedger.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.ConfigureOptions<JwtBearerSetup>();
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// A ordem importa: os tratadores específicos vêm antes do genérico (500).
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<UniqueViolationHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "database");

var app = builder.Build();

// Aplica as migrations pendentes e garante os dados iniciais antes de aceitar requisições.
await using (var scope = app.Services.CreateAsyncScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<WalletDbContext>();
    var clock = services.GetRequiredService<TimeProvider>();

    await db.Database.MigrateAsync();
    await SeedData.EnsureSystemAccountsAsync(db, clock);

    if (app.Configuration.GetValue<bool>("Seed:Demo"))
    {
        await DemoSeed.EnsureDemoDataAsync(db, services.GetRequiredService<IPasswordHasher>(), clock);
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

await app.RunAsync();

public partial class Program;
