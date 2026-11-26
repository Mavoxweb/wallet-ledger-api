# ADR 0004 — Travamento pessimista das contas, sempre em ordem crescente de id

- **Status:** aceito
- **Data:** 26/11/2026

## Contexto

Uma transferência lê o saldo do pagador, confere se é suficiente e grava os novos saldos. Sem proteção, duas transferências simultâneas da mesma conta leem o mesmo saldo e as duas passam:

```
Saldo de A: R$ 100
T1: lê 100 → 100 ≥ 80, ok        T2: lê 100 → 100 ≥ 80, ok
T1: grava 20                     T2: grava 20      → R$ 160 saíram de uma conta com R$ 100
```

Travar as contas resolve isso, mas cria outro risco. Transferências **cruzadas** (A→B e B→A ao mesmo tempo) travando na ordem "pagador, depois recebedor" esperam uma pela outra para sempre:

```
T1 (A→B): trava A … espera B
T2 (B→A): trava B … espera A      → deadlock
```

## Decisão

1. Toda operação que move dinheiro roda numa **transação de banco**.
2. As contas envolvidas são carregadas com **`SELECT … FOR UPDATE`** antes de qualquer leitura de saldo. A trava dura até o commit ou rollback.
3. As contas são travadas **numa única consulta, em ordem crescente de id, decidida pelo PostgreSQL**:

   ```sql
   SELECT * FROM accounts WHERE id = ANY(@ids) ORDER BY id FOR UPDATE
   ```

   Com uma ordem global única, duas transações nunca esperam uma pela outra em ciclo: quem chega segunda espera a primeira terminar, e pronto.

4. A ordenação fica no SQL, **não no C#**: o .NET e o PostgreSQL comparam UUIDs de formas diferentes. Ordenar no C# e travar em consultas separadas reintroduziria ordens inconsistentes.

Como defesa em profundidade, a tabela `accounts` ainda tem um `CHECK (allows_negative OR balance_cents >= 0)` (ver migração inicial).

## Evidência

O teste `Parallel_crossed_transfers_never_overdraw_and_ledger_stays_balanced` dispara 100 transferências simultâneas (50 A→B e 50 B→A) e confere que:
- toda resposta é `201` ou `422` (nunca erro interno);
- nenhum saldo fica negativo;
- a soma dos saldos das duas contas não muda;
- o saldo em cache de cada conta é igual à soma dos lançamentos dela;
- o razão inteiro soma zero.

Como experimento, a trava foi trocada temporariamente por "pagador, depois recebedor" em consultas separadas. O mesmo teste falhou com dezenas de respostas `500`. Isso confirma que o teste detecta o problema e que a ordenação é o que o evita.

## Alternativas consideradas

### Concorrência otimista (coluna de versão / `xmin`)
Lê sem travar e, ao gravar, confere se a versão mudou; se mudou, falha e o cliente tenta de novo. Funciona bem com pouca disputa, mas numa conta muito movimentada as tentativas falham em cascata e exigem lógica de repetição. Como o saldo é exatamente o dado disputado, a trava pessimista é mais simples e previsível.

### Isolamento `SERIALIZABLE`
O PostgreSQL detecta o conflito e aborta uma das transações (`40001`). Também exige repetição no cliente e deixa a regra implícita no nível de isolamento, em vez de explícita no código.

### Atualização condicional (`UPDATE … SET balance = balance - @v WHERE balance >= @v`)
Atômica e rápida para uma conta, mas coloca a regra de negócio no SQL, fora do domínio, e fica complicada quando a operação envolve várias contas e lançamentos.

## Consequências

- Transferências que envolvem a mesma conta são **serializadas**: a segunda espera a primeira. Para uma carteira, é o comportamento correto.
- Contas muito movimentadas viram ponto de disputa (a conta de liquidação nos depósitos, por exemplo). Em escala maior, isso pode ser resolvido com várias contas de liquidação.
- A regra de saldo continua inteiramente no domínio (`Account.Debit`), protegida pela trava.
