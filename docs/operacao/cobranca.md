# Cobrança (planos, teste grátis e Asaas)

Regras comerciais: `claude/plano-comercial.md` (projeto) e `Domain/Cobranca/Planos.cs`
(planos Mico e Empresa, 20 vagas de fundador, teste de 30 dias ou 10 notas em
produção, 7 dias de tolerância para fatura vencida).

## Como funciona

- **Desligada por padrão** (`Cobranca:Ativa=false`): ninguém é bloqueado, o
  teste grátis não começa a contar e não há botão de assinar. É o beta.
- Ligada: o teste começa na **primeira nota em produção** (homologação é
  sempre livre). Acabado o teste sem assinatura, fatura vencida há mais de 7
  dias ou assinatura cancelada: só a **emissão em produção** para (tela, lote e
  automação). Consulta e download nunca são bloqueados.
- **Assinar** (tela Assinatura, só Administrador): cria o cliente e a
  assinatura mensal no Asaas (forma de pagamento à escolha do cliente) e abre a
  primeira fatura, que vence no dia. A conta fica ativa quando o Asaas avisa o
  pagamento pelo webhook.
- **Webhook** `POST /api/webhooks/asaas`: autenticado pelo token do header
  `asaas-access-token`; cada evento é gravado em `eventos_cobranca` com o id do
  Asaas como chave única (evento repetido não reaplica nada).
- **Contas isentas** (`Cobranca:ContasIsentas`): nunca são cobradas. Coloque a
  conta do operador antes de ligar a cobrança.

## Prêmio do "Indique e ganhe" (1 mês grátis, automático)

- **Quando nasce:** no **primeiro pagamento confirmado** (webhook
  `PAYMENT_CONFIRMED` ou `PAYMENT_RECEIVED`) de uma conta que se cadastrou pelo
  link de outra. Nascem dois prêmios: um para a conta indicada e um para quem
  indicou. Tabela `premios_indicacao`, com índice único (conta indicada +
  papel): cada indicação premia cada lado uma única vez, mesmo com evento
  repetido.
- **Como é aplicado** (job `aplicar-premios-indicacao`, disparado logo após o
  pagamento e todo dia às 07:40):
  1. se já existe a próxima fatura em aberto da assinatura (o Asaas gera com até
     40 dias de antecedência), ela é **removida** no Asaas — o cliente não paga
     aquele mês e a assinatura continua;
  2. se ainda não existe, o **próximo vencimento** da assinatura é adiado em 1 mês.
  Vários prêmios da mesma conta somam meses.
- **Prêmio guardado:** só vale para assinatura **em dia** (Ativa). Quem indicou
  e ainda está no beta, no teste, em atraso ou com assinatura cancelada fica com
  o prêmio guardado; ele é aplicado no primeiro pagamento seguinte dessa conta.
  Conta isenta nunca recebe (não há fatura).
- **Nunca em dobro:** o prêmio é gravado como "Aplicando" antes de chamar o
  Asaas. Se o sistema cair no meio, ele vira **Erro** e não é repetido sozinho.
  Recusa do Asaas tenta de novo até 3 vezes; depois, Erro.
- **Operador:** Plataforma › Contas › quadro **Prêmios de indicação**. Para um
  prêmio com Erro: abrir o painel do Asaas, conferir a assinatura da conta
  (Cobranças › assinatura › faturas). Se a próxima fatura **já sumiu** (ou o
  vencimento já foi adiado), **não** clique em "Tentar de novo". Se nada mudou,
  clique em "Tentar de novo". Um prêmio com erro também dispara o e-mail de
  falha de job.
- **Desfazer** (raro): a fatura removida pode ser restaurada no painel do Asaas
  (cobrança removida › restaurar).

## Configuração

| Chave | Onde | Valor |
|---|---|---|
| `Asaas:ApiKey` | user-secrets (PC) / `docker-compose.segredos.yml` (VPS) | chave do Asaas (`$aact_hmlg_...` no sandbox) |
| `Asaas:WebhookToken` | idem | texto aleatório de 32+ caracteres (`openssl rand -hex 32`), o mesmo cadastrado no painel do Asaas |
| `Asaas:BaseUrl` | appsettings / compose | sandbox `https://api-sandbox.asaas.com/v3` (padrão) · produção `https://api.asaas.com/v3` |
| `Cobranca:Ativa` | compose | `false` no beta; `true` quando os planos valerem |
| `Cobranca:ContasIsentas:0` | compose | Id da conta do operador (Plataforma › Contas) |
| `Cobranca:ContasPiloto:0` | compose | Id de uma conta de teste: a cobrança vale só para ela, mesmo com `Ativa=false` |

No PC (fora do Git):

```
cd src\NfseSaaS.Web
dotnet user-secrets set "Asaas:ApiKey" "<chave do sandbox>"
dotnet user-secrets set "Asaas:WebhookToken" "<token>"
dotnet user-secrets set "Cobranca:Ativa" "true"
```

## Painel do Asaas (uma vez por ambiente)

1. **Integrações › Chaves de API**: gerar a chave.
2. **Integrações › Webhooks › Adicionar**: URL `https://micoempresa.com.br/api/webhooks/asaas`,
   o mesmo token de `Asaas:WebhookToken`, versão da API v3, envio sequencial,
   eventos de **Cobranças** (PAYMENT_*, inclusive PAYMENT_DELETED e
   PAYMENT_RESTORED) e de **Assinaturas** (SUBSCRIPTION_*).
3. No sandbox, uma fatura pode ser dada como paga pelo próprio painel do Asaas
   (cobrança › confirmar recebimento), o que dispara o webhook.

O webhook só alcança um endereço público: para testar o ciclo completo, use o
sistema publicado (VPS) com a chave do sandbox.
