# ADR 0005 — Idempotência gravada no PostgreSQL, na mesma transação da operação

- **Status:** aceito
- **Data:** 26/11/2026

## Contexto

Operações que movem dinheiro (depósito, transferência, estorno) não podem ser executadas duas vezes por causa de uma repetição. Repetições acontecem o tempo todo: o app perde a conexão antes de receber a resposta e tenta de novo, o usuário toca duas vezes no botão, um proxy reenvia a requisição.

O padrão de mercado é o cliente mandar um header `Idempotency-Key` (um UUID gerado por operação) e o servidor garantir que a mesma chave produz o mesmo efeito uma única vez, devolvendo a mesma resposta nas repetições.

A questão é **onde guardar a chave** e **como garantir que chave e operação andem juntas**.

## Decisão

A chave fica na tabela `idempotency_keys` do PostgreSQL, com chave primária `(user_id, key)`, e o `INSERT` dela é o **primeiro comando da mesma transação** que grava os lançamentos.

```
BEGIN
  INSERT INTO idempotency_keys (user_id, key, request_hash, ...)   -- 1º comando
  ... trava contas, valida regras, grava transação e lançamentos ...
  UPDATE idempotency_keys SET status_code, response_body
COMMIT
```

Comportamento resultante:

| Situação | O que acontece |
|---|---|
| Primeira requisição | Executa e guarda a resposta junto com os lançamentos |
| Repetição depois do commit | O `INSERT` viola a chave primária → devolve a resposta guardada, byte a byte |
| Duas requisições **simultâneas** com a mesma chave | O `INSERT` da segunda **espera** no índice único até a primeira terminar. Commit → a segunda devolve a resposta guardada. Rollback → a segunda executa normalmente |
| Mesma chave com **corpo diferente** | `422 idempotency_key_reused` (o hash do pedido não bate) |
| A operação falha por regra de negócio | Rollback desfaz tudo, **inclusive a chave**. O cliente pode corrigir e tentar de novo com a mesma chave |
| Entrada inválida (`400`) | Rejeitada antes de abrir a transação. Nada é gravado |

Detalhes:
- O hash é SHA-256 de `método + rota + corpo`.
- A resposta é guardada como `text`, e não `jsonb`: o `jsonb` reordena as chaves do JSON, e a repetição precisa devolver exatamente os mesmos bytes.
- Chaves com mais de 24 horas são apagadas de hora em hora por um `BackgroundService`.

## Alternativas consideradas

### Redis (`SET key NX`)
Rápido e muito usado, mas **não participa da transação do banco**. Sempre sobra uma janela de inconsistência:
- grava a chave no Redis, a operação falha no banco → a chave fica "usada" sem efeito nenhum, e a repetição legítima é recusada;
- a operação comita no banco e o processo cai antes de gravar no Redis → a repetição executa de novo, ou seja, **cobrança duplicada**.

Resolver isso exigiria compensações ou uma *outbox*, com mais complexidade para chegar ao que o banco já oferece de graça.

### Status "em andamento" + `409 Conflict`
Gravar a chave numa transação separada com status `processing` e responder `409` a repetições enquanto a primeira não termina. Exige uma máquina de estados e uma rotina para limpar chaves presas em `processing` quando o processo cai no meio. Com o modelo atômico, o PostgreSQL serializa as duas requisições sozinho, sem estados intermediários.

## Consequências

- **Correção por construção:** chave e lançamentos são gravados juntos ou não são gravados.
- Uma repetição simultânea fica **bloqueada** até a primeira terminar, em vez de receber `409`. Para o cliente é transparente: ele recebe a mesma resposta de sucesso.
- O banco é o único ponto de verdade; não há uma segunda infraestrutura para operar.
- Custo: uma escrita a mais por operação. Irrelevante perto do custo de cobrar duas vezes.
