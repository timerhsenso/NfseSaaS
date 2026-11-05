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
   eventos de **Cobranças** (PAYMENT_*) e de **Assinaturas** (SUBSCRIPTION_*).
3. No sandbox, uma fatura pode ser dada como paga pelo próprio painel do Asaas
   (cobrança › confirmar recebimento), o que dispara o webhook.

O webhook só alcança um endereço público: para testar o ciclo completo, use o
sistema publicado (VPS) com a chave do sandbox.
