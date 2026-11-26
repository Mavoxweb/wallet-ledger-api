# Etapa de build: compila e publica a API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/WalletLedger.Api/WalletLedger.Api.csproj -c Release -o /app

# Etapa final: só o runtime, imagem menor e sem o SDK
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "WalletLedger.Api.dll"]
