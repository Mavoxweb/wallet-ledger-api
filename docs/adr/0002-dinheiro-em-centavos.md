# ADR 0002 — Dinheiro como inteiro em centavos

- **Status:** aceito
- **Data:** 26/11/2026

## Contexto

Ponto flutuante binário não representa frações decimais com exatidão (`0.1 + 0.2 = 0.30000000000000004` em `double`). Num sistema financeiro, um centavo de diferença acumulado é um bug contábil. O mesmo valor também precisa trafegar sem perda entre C#, Dart (app) e TypeScript (backoffice).

## Decisão

- Todo valor monetário é um **inteiro de 64 bits em centavos** (`long`). R$ 12,34 é `1234`.
- No domínio, o tipo `Money` encapsula os centavos e **não tem construtor a partir de `double` ou `decimal`**. Somas e subtrações usam `checked`: estourar o limite do `long` gera erro em vez de valor errado.
- Na API, o campo se chama `amountCents` (e `balanceCents`). O cliente formata para exibir.
- No banco, colunas `bigint`.
- Divisão por 100 só acontece para **exibir** (`Money.ToString()` → `"R$ 1.234,56"`) e para escrever o valor no BR Code, por aritmética inteira (`1050` → `"10.50"`).
- Valores acima de R$ 100 milhões são rejeitados como entrada inválida (`400`), o que também afasta qualquer risco de estouro em somas.

## Alternativas consideradas

- **`decimal` (C#) / `numeric` (PostgreSQL):** exato e muito usado. Rejeitado porque Dart e JavaScript não têm um tipo decimal nativo equivalente: o valor viraria `double` no cliente, ou string que precisa de biblioteca. Inteiro em centavos funciona igual nas três linguagens.
- **`double`:** rejeitado pelo erro de representação.

## Consequências

- Nenhum arredondamento acontece em lugar nenhum do sistema.
- Moedas com mais de 2 casas decimais exigiriam outra unidade. Fora do escopo: o sistema é só em BRL.
