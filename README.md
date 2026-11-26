# Wallet Ledger API

[![CI](https://github.com/Mavoxweb/wallet-ledger-api/actions/workflows/ci.yml/badge.svg)](https://github.com/Mavoxweb/wallet-ledger-api/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-4169E1?logo=postgresql&logoColor=white)

API de uma **carteira digital** com **livro-razão de partidas dobradas**: contas, depósitos, transferências PIX por chave, extrato e QR code de cobrança no padrão BR Code.

O foco é **correção**: nenhum saldo negativo sob concorrência, nenhuma cobrança duplicada em repetições e cada centavo rastreável.

> Faz parte do **Wallet Ledger**, um sistema com três repositórios:
> - **wallet-ledger-api**: esta API (.NET)
> - **wallet-ledger-mobile**: app do correntista em Flutter *(em breve)*
> - **wallet-ledger-backoffice**: painel da operação em Angular *(em breve)*

---

## Destaques técnicos

| Pergunta | Resposta | Onde |
|---|---|---|
| Como evita saldo negativo com transferências simultâneas? | Transação + `SELECT … FOR UPDATE`, sempre em ordem crescente de id decidida pelo PostgreSQL, o que também evita deadlock. Teste com **100 PIX cruzados simultâneos** | [ADR 0004](docs/adr/0004-ordem-de-travamento.md) |
| O que acontece se o cliente repetir a requisição? | `Idempotency-Key` obrigatório, gravado **na mesma transação** dos lançamentos. Repetição devolve a mesma resposta, byte a byte | [ADR 0005](docs/adr/0005-idempotencia-no-postgres.md) |
| Como garante que nenhum dinheiro some ou aparece? | Partidas dobradas: todo lançamento tem contrapartida, a soma global é sempre zero, e nada é editado (só estornado) | [ADR 0003](docs/adr/0003-partidas-dobradas.md) |
| Como representa dinheiro? | Inteiro em centavos. Sem `double` em lugar nenhum | [ADR 0002](docs/adr/0002-dinheiro-em-centavos.md) |
| Como organiza o código? | Clean Architecture em 4 projetos, regras no domínio, sem camadas desnecessárias | [ADR 0001](docs/adr/0001-clean-architecture.md) |
| Como os clientes consomem a API sem quebrar? | Contrato OpenAPI gerado do código, verificado no CI e publicado por versão | [ADR 0006](docs/adr/0006-contrato-gerado-do-codigo.md) |

Outros pontos:
- **Refresh token com rotação e detecção de reuso**: um token roubado e reaproveitado derruba todas as sessões do usuário.
- **Defesa em profundidade**: além das regras no domínio, o banco tem `CHECK` de saldo não negativo, índices únicos e chaves estrangeiras.
- **BR Code** implementado do zero e validado contra o exemplo oficial do Banco Central (CRC16 incluso).
- **Testes contra PostgreSQL real** (Testcontainers), sem banco em memória.

## Arquitetura

```
   App Flutter (correntista)            Backoffice Angular (operação)
              │                                      │
              └──────────── HTTPS + JWT ─────────────┘
                                 │
                    ASP.NET Core 10 Web API
          Api → Infrastructure → Application → Domain
                                 │
                   PostgreSQL 17 (razão, contas,
                 chaves PIX, idempotência, tokens)
```

```
src/
├── WalletLedger.Domain/          regras puras: Money, Account, LedgerTransaction, PixKey, BrCode
├── WalletLedger.Application/     casos de uso (um handler por operação) e validação
├── WalletLedger.Infrastructure/  EF Core, migrations, JWT, idempotência, travamento de contas
└── WalletLedger.Api/             controllers, filtros, erros (ProblemDetails), OpenAPI
tests/
├── WalletLedger.Domain.Tests/        testes unitários do domínio
└── WalletLedger.IntegrationTests/    API + PostgreSQL real em contêiner
contracts/openapi.json                contrato gerado a cada build
docs/adr/                             decisões de arquitetura
```

## Como rodar

Pré-requisito: **Docker**.

```bash
docker compose up --build
```

- API: http://localhost:8080
- Documentação interativa: http://localhost:8080/docs
- Saúde: http://localhost:8080/health

O banco é criado e migrado automaticamente, já com contas de demonstração:

| E-mail | Senha | Perfil | Saldo inicial | Chave PIX |
|---|---|---|---|---|
| `ana@walletledger.dev` | `Demo@1234` | Cliente | R$ 2.500,00 | `ana@walletledger.dev` |
| `bruno@walletledger.dev` | `Demo@1234` | Cliente | R$ 2.500,00 | `bruno@walletledger.dev` |
| `operador@walletledger.dev` | `Demo@1234` | Operador | — | — |
| `admin@walletledger.dev` | `Demo@1234` | Admin | — | — |

O arquivo [`requests.http`](requests.http) tem o roteiro completo (login → saldo → PIX → extrato → QR code) pronto para rodar no VS Code ou no Rider. Também dá para importar `contracts/openapi.json` no Postman.

## Endpoints

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/v1/auth/register` | Cadastro de cliente (abre a conta) |
| `POST` | `/api/v1/auth/login` | Access token (15 min) + refresh token (30 dias) |
| `POST` | `/api/v1/auth/refresh` | Rotação do refresh token |
| `POST` | `/api/v1/auth/logout` | Revoga o refresh token |
| `GET` | `/api/v1/me` | Perfil de quem está logado |
| `GET` | `/api/v1/account` | Saldo e status |
| `GET` | `/api/v1/account/statement` | Extrato com paginação por cursor |
| `POST` | `/api/v1/deposits/simulated` | Depósito de demonstração · `Idempotency-Key` |
| `GET` `POST` `DELETE` | `/api/v1/pix/keys` | Chaves PIX da conta (e-mail, celular, aleatória) |
| `GET` | `/api/v1/pix/keys/lookup` | Dono da chave (nome e CPF mascarados) |
| `POST` | `/api/v1/transfers/pix` | Transferência por chave · `Idempotency-Key` |
| `POST` | `/api/v1/pix/charges` | QR code de cobrança (BR Code) |

Valores sempre em **centavos** (`amountCents`). Erros no formato **ProblemDetails** (RFC 9457), com um `code` estável para regras de negócio (`insufficient_funds`, `account_not_active`, `self_transfer`…).

## Como testar

Pré-requisitos: **.NET 10 SDK** e **Docker em execução** (os testes de integração sobem um PostgreSQL descartável).

```bash
dotnet test
```

São 133 testes: 54 de domínio e 79 de integração. Entre eles:
- 100 transferências cruzadas simultâneas: nenhum saldo negativo, nenhum deadlock, razão somando zero;
- requisições paralelas com a mesma chave de idempotência: exatamente uma transação;
- limite diário de depósito virando à meia-noite de Brasília (relógio controlado nos testes);
- detecção de reuso de refresh token;
- QR code comparado com o exemplo oficial do Manual do BR Code.

## Stack

.NET 10 · ASP.NET Core · EF Core 10 + Npgsql · PostgreSQL 17 · FluentValidation · JWT · Serilog · Scalar · xUnit · Testcontainers · Docker · GitHub Actions
