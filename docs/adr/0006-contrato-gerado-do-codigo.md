# ADR 0006 — Contrato OpenAPI gerado do código e publicado por versão

- **Status:** aceito
- **Data:** 26/11/2026

## Contexto

A API é consumida por dois clientes em repositórios separados: o app Flutter (`wallet-ledger-mobile`) e o backoffice Angular (`wallet-ledger-backoffice`). Os modelos e chamadas desses clientes precisam acompanhar a API sem serem escritos à mão, e uma mudança na API não pode quebrar os clientes de surpresa.

## Decisão

- **O código vem primeiro.** O contrato é gerado dos controllers (geração nativa do ASP.NET Core, com os comentários `///` virando descrições) e gravado em `contracts/openapi.json` **a cada build**.
- **O CI falha se o contrato commitado estiver desatualizado:** depois do build, `git diff --exit-code contracts/openapi.json`.
- **Cada versão publicada** (tag `vX.Y.Z`) gera um GitHub Release com o `openapi.json` anexado.
- **Os clientes fixam uma versão** do contrato, baixam o arquivo daquela release e geram seus clientes (`openapi-generator`: `dart-dio` no app e `typescript-angular` no backoffice). Atualizar a API não afeta os clientes até eles subirem de versão, por PR.

Detalhes do contrato:
- esquema `Bearer` (JWT) nos endpoints autenticados;
- header `Idempotency-Key` documentado como obrigatório nos endpoints que movem dinheiro;
- enums como texto em camelCase (`"email"`, `"transfer"`), igual ao que a API trafega;
- documentação interativa em `/docs` nos ambientes de desenvolvimento e demonstração.

## Alternativas consideradas

- **Contrato escrito à mão primeiro (design-first):** bom quando várias equipes negociam a API antes de existir código. Aqui, com uma única API e o mesmo autor, o contrato manual tenderia a divergir do código.
- **Clientes lendo o contrato direto da `main`:** qualquer mudança na API quebraria o build dos clientes no mesmo instante. Versões fixas dão controle sobre quando atualizar.

## Consequências

- O contrato nunca mente: ele é o código.
- Mudanças de API aparecem como diff do `openapi.json` no próprio PR, fáceis de revisar.
- A geração no build carrega a aplicação sem banco nem segredos. Por isso, as tarefas de inicialização (migrations, seed, validação do JWT) são puladas quando quem carrega é a ferramenta de geração.
