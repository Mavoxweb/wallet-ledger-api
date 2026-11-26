# Wallet Ledger API

API de uma carteira digital com livro-razão de partidas dobradas: contas, depósitos, transferências estilo PIX e extrato.

## Como rodar

Pré-requisito: Docker.

```bash
docker compose up --build
```

A API sobe em `http://localhost:8080`. Verificação de saúde: `GET /health`.

## Como testar

Pré-requisitos: .NET 10 SDK e Docker em execução (os testes de integração sobem um PostgreSQL descartável).

```bash
dotnet test
```
