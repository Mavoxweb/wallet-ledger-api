# ADR 0001 — Clean Architecture em quatro projetos, sem camada de repositórios

- **Status:** aceito
- **Data:** 26/11/2026

## Contexto

A API concentra regras financeiras (saldo, partidas dobradas, limites) que precisam ser fáceis de testar e não podem depender de detalhes de framework. Ao mesmo tempo, o projeto é pequeno: camadas demais custam mais do que protegem.

## Decisão

Quatro projetos, com dependências apontando sempre para dentro:

```
Api → Infrastructure → Application → Domain
```

| Projeto | Contém | Depende de |
|---|---|---|
| `Domain` | Entidades e regras (`Money`, `Account`, `LedgerTransaction`, `PixKey`, `BrCode`) | nada |
| `Application` | Casos de uso (um handler por operação), validação, contratos de infraestrutura | Domain, EF Core (abstrações) |
| `Infrastructure` | `DbContext`, mapeamentos, migrations, JWT, idempotência, travamento | Application |
| `Api` | Controllers, filtros, tratamento de erros, OpenAPI | Infrastructure |

Escolhas pragmáticas:
- **Sem repositórios genéricos.** Os handlers usam o EF Core através de `IWalletDbContext`. O `DbContext` já é uma unidade de trabalho com repositórios (`DbSet`); outra camada por cima só repetiria a mesma API.
- **Sem MediatR.** Cada controller recebe o handler por injeção (`[FromServices]`). É explícito, fácil de navegar e sem dependência que mudou de licença.
- **Regras no domínio.** `Account.Debit` recusa saldo negativo; `LedgerTransaction.Create` recusa lançamentos que não somam zero. Nenhum caminho de código consegue contornar isso.

## Consequências

- O domínio é testado em milissegundos, sem banco (testes em `WalletLedger.Domain.Tests`).
- A camada de aplicação conhece `DbSet` e LINQ do EF Core. Trocar de ORM exigiria mexer nos handlers, um custo aceito em troca de menos código.
- Operações que dependem do banco de verdade (travas, unicidade, transações) são testadas com PostgreSQL real via Testcontainers.
