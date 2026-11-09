# Cobrança (planos, teste grátis e Asaas)

Regras comerciais: `claude/plano-comercial.md` (projeto). Planos, preços e
vagas de fundador ficam no banco e são administrados em **Plataforma › Planos**
(ver abaixo). Fixos no código (`Domain/Cobranca/Planos.cs`): teste de 30 dias
(sem limite de notas), reserva de fundador até 7 dias depois do fim do teste e
10 dias de tolerância para fatura vencida (a emissão bloqueia no 11º dia; os
Termos de uso, item 8, citam o mesmo prazo).

## Planos e preços (Plataforma › Planos)

- **Planos iniciais** (criados no startup por `PlanosSeeder` **só com o catálogo
  vazio**, ou seja, banco novo): Mico (1), Empresa (2), Escritório 10 (3),
  Escritório 25 (4) e Escritório 50 (5). Depois disso o catálogo é do operador:
  plano excluído não volta e mudar o seeder não altera o banco. As vagas de
  fundador são criadas se faltar o grupo.
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
  Escritórios (10), editáveis, com **data-fim da campanha** opcional (inclusive).
  Usada = assinatura com preço de fundador em plano do grupo (cancelada também
  conta) + **reserva válida** de quem ainda não assinou. Diminuir não tira o
  preço de quem já é fundador nem de quem já reservou.

## Um teste grátis por CNPJ

- Quando o teste de uma conta de perfil **Empresa** começa (primeira nota em
  produção ou assinatura antes dela), os CNPJs das empresas da conta (ativas e
  desativadas) vão para `testes_gratis_cnpj` (chave = CNPJ).
- Se algum desses CNPJs já está lá em nome de **outra** conta, a conta fica
  `assinaturas.SemTesteGratis = true`: a emissão em produção só com assinatura,
  e quem assina tem a primeira fatura vencendo no dia (com a tolerância normal).
- A tabela não tem FK e **não é apagada na exclusão de conta**: excluir e criar
  outra conta não devolve o teste.
- **Contas de contador** não entram (cadastram CNPJs de clientes, que podem ter
  conta própria): nunca perdem o teste e não registram CNPJ.
- Engano (ex.: CNPJ vendido ou conta antiga abandonada): **Plataforma › Contas ›
  Cobrança › Liberar novo teste** (o CNPJ passa a constar como desta conta).

## Ferramentas do operador (Plataforma › Contas)

Menu **Cobrança** (ícone de cartão) em cada conta **sem assinatura viva no
Asaas** (conta com fatura no Asaas se ajusta pelo painel do Asaas). Tudo pede
motivo e vai para o Histórico da conta (`ProrrogarTeste`, `LiberarNovoTeste`,
`IsentarConta`, `RemoverIsencao`), com o operador.

- **Prorrogar teste:** soma dias ao teste (1 a 90 por vez, até 180 no total;
  `assinaturas.TesteDiasExtras`). Estende também a reserva de fundador. Se o
  teste já acabou, só aceita se o novo fim ficar no futuro.
- **Liberar novo teste:** 30 dias a partir de hoje, zera os dias extras e tira
  o "sem teste por CNPJ". Não vale para quem já foi assinante (cancelada).
- **Isentar:** emite em produção sem teste e sem assinatura até a data
  (inclusive) ou sem prazo (`IsentaPelaPlataforma`, `IsentaAte`, `MotivoIsencao`).
  Sem avisos de cobrança enquanto isenta. Se a conta assinar, a isenção acaba e
  a primeira fatura vence no dia seguinte ao fim da isenção. As contas de
  `Cobranca__ContasIsentas` (VPS) continuam valendo e aparecem como "Isenta (VPS)".
- **Números de venda** no topo: em teste agora (e quantos acabaram sem assinar),
  assinantes e fundadores, MRR (soma do preço de quem está em dia ou em atraso),
  conversão (assinantes ÷ contas que começaram o teste, sem as isentas) e vagas
  de fundador sobrando/reservadas. Filtro por situação na lista.

## Campanha de fundador (reserva para quem entra)

- **Quando a vaga é guardada:** ao confirmar o e-mail (cadastro), ao abrir a tela
  Assinatura e na varredura diária do job `avisar-cobranca` (pega quem confirmou
  por convite ou senha redefinida, e as contas antigas). Só para conta ativa, com
  e-mail confirmado, que nunca assinou, não isenta, e enquanto a campanha do grupo
  está aberta (sobra vaga e não passou da data-fim). Grupo pelo perfil: contador
  → Escritórios; empresa → Empresas. Mais antigas primeiro na varredura.
- **Validade:** até 7 dias depois do fim do teste grátis. Reserva feita com a
  cobrança desligada para a conta (beta) **vale sem prazo**. Teste que não
  começou: o prazo ainda não corre.
- **Ao assinar:** fundador pela reserva (mesmo sem vaga sobrando) se o plano é do
  grupo da reserva; senão, só se sobrar vaga. A reserva é consumida (limpa) ao
  assinar. Cancelar perde o preço de fundador; voltando, paga o preço do dia.
- Campos em `assinaturas`: `FundadorReservadoEm`, `FundadorReservaGrupo`,
  `FundadorReservaSemPrazo`.
- **Fora de venda**: o plano some da tela Assinatura; quem já assina continua nele.
- **Excluir** (lixeira, só aparece quando pode): só plano que **nunca foi assinado**
  (nem assinatura cancelada, que guarda o código dele); vale também para os
  iniciais. Já assinado: "Fora de venda". Pede confirmação e envia um código de 6 dígitos ao **seu e-mail**
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
- Ligada: o teste (30 dias, sem limite de notas) começa na **primeira nota em
  produção** ou ao assinar, o que vier primeiro (homologação é sempre livre).
  Acabado o teste sem assinatura, fatura vencida há mais de 10 dias ou assinatura
  cancelada (depois dos dias pagos): só a **emissão em produção** para (tela, lote
  e automação). Consulta e download nunca são bloqueados.
- **Assinar** (tela Assinatura, só Administrador): cria o cliente e a
  assinatura mensal no Asaas (forma de pagamento à escolha do cliente). A
  **primeira fatura vence no dia em que o teste acaba** (`assinaturas.PrimeiraCobrancaEm`);
  depois do teste, vence no dia. Sem pagar, a emissão segue até esse vencimento
  mais a tolerância de 10 dias. A conta fica ativa quando o Asaas avisa o
  pagamento pelo webhook.
- **Cancelar** (tela Assinatura): sem multa. A emissão continua até o fim do
  teste ou do mês já pago (véspera do próximo vencimento lido no Asaas antes de
  cancelar; `assinaturas.AcessoAteAposCancelar`). Em atraso, para na hora.
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

## NFS-e da mensalidade (emitida pela LFMS)

Cada mensalidade paga no Asaas vira uma NFS-e emitida **pela própria
plataforma** (a empresa LFMS, cadastrada dentro da conta do operador), com a
conta que pagou como tomadora. **Desligado** enquanto as duas chaves abaixo
estiverem vazias.

Como funciona:

- O webhook grava o pagamento (PAYMENT_CONFIRMED / PAYMENT_RECEIVED) e agenda o
  job `emitir-notas-mensalidade`, que também roda todo dia às 08:10 como
  repescagem. Um pagamento = uma nota (os dois eventos do mesmo pagamento não
  duplicam; a chave de idempotência é `mensalidade:<id do pagamento>`).
- Ficam de fora: a própria conta emissora, as contas isentas, as contas com a
  cobrança desligada e pagamentos de valor zero. Ao ligar, só entram os
  pagamentos dos últimos 35 dias (ou a partir de `EmitirAPartirDe`).
- **Tomador**: o cliente da LFMS com o mesmo CPF/CNPJ da conta. Se não existe, é
  criado com o endereço da empresa da própria conta (mesmo CNPJ), o e-mail do
  administrador e "enviar a nota por e-mail" ligado. Sem endereço (ex.: conta
  de contador sem empresa com o CNPJ dela), a nota fica com erro: cadastre o
  cliente na tela **Clientes** da LFMS e a próxima passada emite.
- **Item da LC 116 configurável**: a nota usa o Serviço indicado em
  `NotaMensalidade__ServicoId`. O item (sugestão: **01.05**, licenciamento de
  programas — código nacional 010501), o NBS e o IBS/CBS (sugestão cIndOp
  **100501** / cClassTrib **000001**) ficam no cadastro desse Serviço: trocar o
  item é editar o Serviço (ou apontar para outro), sem mexer no código.
  Confirmar o item e o NBS com o contador.
- Falha (SEFIN fora, rejeição, cadastro incompleto): alerta por e-mail ao
  operador e nova tentativa na próxima passada, até 8 vezes. Rejeição que
  permite reenvio é reenviada sozinha.
- Pagamento desfeito (estorno/chargeback) **antes** da nota: não emite.
  **Depois** da nota: alerta ao operador uma vez — cancele a nota na tela de
  Notas da LFMS.

### Ligar (VPS)

1. Na conta do operador, em **Empresas**, a LFMS com certificado e o ambiente
   desejado (comece em **Homologação**).
2. Em **Serviços** da LFMS, cadastre "Licença de uso do sistema" com o item
   01.05 (ou o que o contador indicar).
3. Na VPS, pegue os dois Ids (troque o CNPJ pelo da LFMS, só números):

   ```
   docker exec nfsesaas-postgres psql -U nfse -d nfse_saas -c "select e.\"Id\" as empresa, s.\"Id\" as servico, s.\"Descricao\", s.\"CodigoTributacaoNacional\" from empresas e join servicos s on s.\"EmpresaId\" = e.\"Id\" where e.\"Cnpj\" = '00000000000000';"
   ```

4. No `docker-compose.segredos.yml` (ou no compose), em `environment:` do app:

   ```
   NotaMensalidade__EmpresaId: "<Id da empresa LFMS>"
   NotaMensalidade__ServicoId: "<Id do serviço>"
   NotaMensalidade__EmitirAPartirDe: "2026-12-01"   # opcional
   ```

5. Reinicie o app. Para desligar, apague as linhas e reinicie.

Teste em homologação: com a LFMS em Homologação e uma conta piloto
(`Cobranca:ContasPiloto`), confirme uma fatura no sandbox do Asaas; a nota
aparece em Notas fiscais da LFMS em poucos segundos. Antes de passar a LFMS
para Produção, use `EmitirAPartirDe` com a data da virada, para nada do teste
se repetir.

## Configuração

| Chave | Onde | Valor |
|---|---|---|
| `Asaas:ApiKey` | user-secrets (PC) / `docker-compose.segredos.yml` (VPS) | chave do Asaas (`$aact_hmlg_...` no sandbox) |
| `Asaas:WebhookToken` | idem | texto aleatório de 32+ caracteres (`openssl rand -hex 32`), o mesmo cadastrado no painel do Asaas |
| `Asaas:BaseUrl` | appsettings / compose | sandbox `https://api-sandbox.asaas.com/v3` (padrão) · produção `https://api.asaas.com/v3` |
| `Cobranca:Ativa` | compose | `false` no beta; `true` quando os planos valerem |
| `Cobranca:ContasIsentas:0` | compose | Id da conta do operador (Plataforma › Contas) |
| `Cobranca:ContasPiloto:0` | compose | Id de uma conta de teste: a cobrança vale só para ela, mesmo com `Ativa=false` |
| `NotaMensalidade:EmpresaId` / `NotaMensalidade:ServicoId` | compose | empresa e serviço da LFMS que emitem a NFS-e da mensalidade (vazio = desligado) |
| `NotaMensalidade:EmitirAPartirDe` | compose | opcional, `AAAA-MM-DD`: pagamentos antes disso não geram nota |

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
