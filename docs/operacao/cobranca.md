# Cobrança (planos, teste grátis e Asaas)

Regras comerciais: `claude/plano-comercial.md` (projeto). Planos, preços e
vagas de fundador ficam no banco e são administrados em **Plataforma › Planos**
(ver abaixo). Fixos no código (`Domain/Cobranca/Planos.cs`): teste de 30 dias ou
10 notas em produção e 7 dias de tolerância para fatura vencida.

## Planos e preços (Plataforma › Planos)

- **Planos iniciais** (criados no startup por `PlanosSeeder`, só se faltarem):
  Mico (1), Empresa (2), Escritório 10 (3), Escritório 25 (4) e Escritório 50 (5).
  Depois de criados, quem manda é a tela: mudar o seeder não altera o banco.
- **Aparece para**: empresas, contadores ou ambos (pelo perfil da conta).
  Contador vê Empresa + Escritórios; empresa vê Mico + Empresa.
- **Preço com histórico** (`precos_planos`): cada mudança é uma linha nova com
  "vale a partir de" (hoje ou data futura, nunca passada). Vale o preço com a
  data mais recente até hoje; o futuro aparece como "Agendado". Cadastrar de novo
  na **mesma data** corrige aquele preço (fica no Histórico do operador).
- **Quem já assina mantém o preço contratado** (`assinaturas.PrecoMensal` e o
  valor da assinatura no Asaas). O preço novo vale só para novas assinaturas.
  Reajuste de assinantes atuais: ainda não automatizado (precisa aviso de 30 dias).
- **Vagas de fundador separadas por grupo** (`vagas_fundador`): Empresas (20) e
  Escritórios (10), editáveis. Usada = assinatura com preço de fundador em plano
  do grupo (cancelada também conta). Diminuir não tira o preço de quem já é fundador.
- **Fora de venda**: o plano some da tela Assinatura; quem já assina continua nele.
- **Excluir** (lixeira, só aparece quando pode): só plano que **nunca foi assinado**
  (nem assinatura cancelada) e que **não é inicial** (códigos 1 a 5, que o seeder
  recriaria). Pede confirmação e envia um código de 6 dígitos ao **seu e-mail**
  (o mesmo esquema da exclusão de conta: 10 minutos, uso único, 5 tentativas,
  direto pelo SMTP, sem gravar no EmailLog). Apaga o plano e o histórico de
  preços; na Auditoria fica `ExcluirPlano` com os preços que existiam.
- O grupo de fundador de um plano não muda depois que ele tem assinaturas.
- Limites (empresas, notas/mês; em branco = sem limite) são **suaves**: servem
  para avisar e orientar troca de plano, nunca travam a emissão.
- Toda alteração da tela vai para o **Histórico** (auditoria) do operador:
  `CriarPlano`, `AlterarPlano`, `NovoPrecoPlano`, `CorrigirPrecoPlano`, `AlterarVagasFundador`, `ExcluirPlano`.

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
  Asaas. Se o sistema cair no meio, depois de 15 minutos ele vira **Erro** e
  não é repetido sozinho.
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
