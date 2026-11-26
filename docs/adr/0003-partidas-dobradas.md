# ADR 0003 — Livro-razão de partidas dobradas com lançamentos imutáveis

- **Status:** aceito
- **Data:** 26/11/2026

## Contexto

Guardar só um campo "saldo" e alterá-lo a cada operação é simples, mas não deixa rastro: não dá para explicar como se chegou a um saldo, recalcular o saldo de uma data passada, nem detectar se algum valor foi criado ou perdido por um bug.

## Decisão

- Cada movimentação é uma **transação** com **dois ou mais lançamentos** (`postings`) que **somam exatamente zero**. Crédito é positivo, débito é negativo.

  | Operação | Lançamentos |
  |---|---|
  | PIX de R$ 100 de A para B | A −10000, B +10000 |
  | Depósito de R$ 50 em A | A +5000, `liquidacao-externa` −5000 |

- A conta de sistema `liquidacao-externa` representa o dinheiro que entra de fora. É a única conta que pode ficar negativa.
- **Lançamentos são imutáveis**: não existe UPDATE nem DELETE. Correção é feita por **estorno**, que cria lançamentos opostos e marca a transação original como `reversed`.
- `LedgerTransaction.Create` recusa conjuntos que não somam zero ou que têm menos de dois lançamentos.
- O saldo da conta (`accounts.balance_cents`) é um **cache** da soma dos lançamentos, atualizado na mesma transação e sob trava (ADR 0004). A fonte da verdade é o razão.

## Consequências

- **Invariante auditável:** a soma de todos os lançamentos do sistema é sempre zero. Os testes de concorrência conferem isso, junto com "saldo em cache = soma dos lançamentos da conta".
- Histórico completo: qualquer saldo de qualquer momento pode ser recalculado.
- O extrato sai direto dos lançamentos, ordenados pelo id sequencial.
- Custo: duas linhas por movimentação em vez de uma atualização. Irrelevante perto do ganho em rastreabilidade.
