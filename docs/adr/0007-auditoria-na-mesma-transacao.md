# ADR 0007 — Ações da operação auditadas na mesma transação, e estorno como transação nova

- **Status:** aceito
- **Data:** 26/11/2026

## Contexto

O backoffice deixa a equipe de operação agir sobre o dinheiro e as contas dos clientes: bloquear, desbloquear e estornar. Numa carteira digital, toda ação desse tipo precisa responder depois a quatro perguntas: quem fez, quando, por quê e o que mudou. Se ação e registro puderem se separar, a trilha deixa de servir como prova: um bloqueio sem registro, ou um registro de um estorno que falhou, é pior do que nenhuma trilha.

O estorno tem uma tensão a mais: o razão é imutável (ADR 0003), mas o estorno precisa desfazer um movimento.

## Decisão

- **A auditoria é gravada na mesma transação do banco da ação.** Bloqueio, desbloqueio e estorno abrem a transação, travam o que vão mudar, aplicam a mudança, adicionam a `AuditEntry` e só então fazem o commit. Se qualquer parte falhar, nada fica.
- **A entrada de auditoria só é criada, nunca alterada.** Ela guarda ator, perfil, ação, conta, transação (no estorno), motivo e um `details` em `jsonb` (status anterior e novo, ou valor e contas do estorno).
- **O motivo é obrigatório:** de 10 a 500 caracteres depois do `trim`. Ação administrativa sem justificativa não passa da validação.
- **O estorno é uma transação nova** do tipo `Reversal`, com os lançamentos opostos aos da original. A original não é apagada nem tem os lançamentos editados: só muda de `completed` para `reversed` e passa a apontar para o estorno (`reversed_by_id`).
- **Dois estornos simultâneos da mesma transação:** a linha da transação original é travada (`SELECT … FOR UPDATE`) **antes** de conferir o status. O segundo pedido espera o commit do primeiro, lê `reversed` e recebe `409 transaction_already_reversed`. Depois disso, as contas são travadas na ordem crescente de id (ADR 0004).
- **O estorno respeita saldo.** Quem devolve o dinheiro precisa ter saldo (`422 insufficient_funds`). Só a conta de sistema (liquidação) pode ficar negativa. O estorno vale mesmo com a conta bloqueada, porque devolver o dinheiro de um golpe é justamente o caso de uso.
- **O estorno é idempotente** (`Idempotency-Key`, ADR 0005) e exclusivo do perfil Admin. O operador consulta, bloqueia e desbloqueia, mas não move dinheiro.

## Alternativas consideradas

- **Auditoria depois do commit** (evento, fila ou log de aplicação): desacopla, mas abre a janela em que a ação aconteceu e o registro se perdeu (queda entre um passo e outro). Com o outbox, que já está no roadmap, a trilha poderia ser *publicada* para fora de forma assíncrona; a *gravação* continua junto da ação.
- **Estorno por `UPDATE`/`DELETE` dos lançamentos:** simples, mas apaga a história. O extrato deixaria de mostrar que o Pix existiu, e a soma do razão em qualquer momento do passado não poderia mais ser reconstituída.
- **Estorno forçado, ignorando saldo:** resolveria o caso "o destino já gastou", mas deixaria uma conta de cliente negativa, coisa que o `CHECK` do banco proíbe por desenho. Quando não há saldo, o caminho correto é um processo fora do sistema (cobrança, bloqueio cautelar), não uma conta negativa.

## Consequências

- A trilha de auditoria é confiável por construção: existe registro se, e somente se, a ação aconteceu.
- O extrato mostra o Pix original (marcado como estornado) e o estorno, como um extrato de banco.
- Um estorno de quem não tem mais saldo é recusado. A operação vê o `insufficient_funds` e resolve fora do sistema.
- Cada ação administrativa custa uma linha a mais na mesma transação. É um custo desprezível para operações raras e manuais.
