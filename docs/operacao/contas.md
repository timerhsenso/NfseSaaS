# Contas (Plataforma › Contas)

Painel do operador da plataforma: todas as contas (clientes do micoempresa),
com números de uso, situação da cobrança e prêmios de indicação.

## Perfil e documento da conta

Desde o cadastro em etapas, cada conta tem um **perfil**:
- **Empresa**: a própria empresa emite as notas. O documento é o CNPJ dela, e
  a conta nasce com essa Empresa cadastrada (dados da Receita; Inscrição
  Municipal e regime ficam para o cliente completar).
- **Contador** (selo "Contador" na lista): contador autônomo (CPF) ou
  escritório (CNPJ). As Empresas são as dos clientes, cada uma com o próprio
  certificado A1.

Contas sem **e-mail confirmado** não emitem em produção. Se um cliente disser
que não recebeu o link, ele usa "Reenviar e-mail" no aviso do topo; confira
também a tela E-mails (tipo "Confirmação de e-mail").

## Suspender × Excluir

| | Suspender | Excluir |
|---|---|---|
| Quando usar | Inadimplência, pedido temporário, suspeita de uso indevido | Conta de teste, cadastro feito por engano, pedido do cliente de apagar tudo |
| Volta atrás? | Sim (Reativar) | **Não** |
| Dados | Ficam guardados | Apagados do banco e do disco |
| Pode com notas? | Sim | **Não**: só conta que nunca registrou nenhuma nota |

## Excluir conta (definitivo)

O botão de lixeira aparece só para conta sem nota autorizada; o servidor confere
as regras completas de novo em cada passo.

1. Clique na lixeira da conta e confirme.
2. O sistema confere as regras e envia um **código de 6 números para o SEU
   e-mail** (o do operador logado). O código vale **10 minutos**, só uma vez, e
   aceita **5 tentativas** (depois é cancelado e é preciso pedir outro).
3. Digite o código no quadro que abre e clique em **Excluir conta**.

### Regras (a exclusão é recusada quando)
- a conta já registrou **qualquer nota** (homologação ou produção, inclusive
  rejeitada ou cancelada): nota é documento fiscal. Use **Suspender**;
- a conta tem **assinatura ativa no Asaas**: cancele a assinatura antes;
- é a **sua própria conta** ou tem um operador da plataforma.

### O que é apagado
Empresas, clientes, serviços, contratos (com histórico de valores e
documentos), configuração da Nota Mensal (e o job agendado), certificados
digitais, usuários (com papéis e sessões), grupos e permissões, histórico
(AuditLog), e-mails enviados, aceites dos termos, assinatura cancelada,
eventos de cobrança e prêmios de indicação ligados à conta. Contas que se
cadastraram pelo link dela continuam existindo, sem o "indicada por".

Fica só um registro no **Histórico da conta do operador**
(`ContaExcluidaPelaPlataforma`), com o Id e as quantidades apagadas, sem nome
nem CNPJ.

### Observações
- O código **não é gravado** em lugar nenhum (nem no EmailLog): sai direto pelo
  SMTP, e o servidor guarda só o hash, em memória. Reiniciar o sistema
  invalida o código (é só pedir outro).
- O **cliente no Asaas** (cadastro e histórico de cobranças) continua no Asaas:
  é registro financeiro do gateway.
- Os **backups** já feitos ainda contêm a conta até saírem da retenção
  (7 diários + 4 semanais, ver `backup-restauracao.md`).

## Encerrar conta (apaga os dados em 90 dias)

Diferente de **Excluir** (na hora, tudo) e de **Suspender** (só fecha o acesso):

- **Quem encerra:** o administrador da conta, em *Meus dados* (código por
  e-mail), ou o operador, em Plataforma › Contas (botão com a porta, com motivo).
- **Ao encerrar:** a assinatura no Asaas é cancelada, o acesso fecha na hora,
  os administradores recebem um e-mail e o expurgo fica marcado para 90 dias.
- **Reabrir:** "Reabrir" (no lugar de "Reativar") desfaz, enquanto os dados
  não foram apagados.
- **Expurgo** (job `expurgar-contas-encerradas`, 05:10): conta sem nota some
  inteira; conta com notas guarda só os documentos fiscais.

### Encerramento automático de quem cancelou (DESLIGADO)

Job `encerrar-contas-canceladas` (05:00). Só funciona com a configuração
`Encerramento__AutomaticoAposCancelarDias` preenchida na VPS (vazio = desligado;
menos de 30 vira 30). **Não ligar antes de Termos novos em vigor**: os Termos
1.1 (item 8.5) prometem que, depois de cancelar, a conta continua disponível.

Quando ligado, para cada conta com a assinatura cancelada (fora isentas,
operadores, suspensas, beta com cobrança desligada):

1. data prevista = fim do acesso pago (ou o dia do cancelamento) + N dias;
2. 15 dias antes, e-mail "sua conta será encerrada em DD/MM" com os links
   *Meus dados* (exportar) e *Assinatura* (assinar de novo). Se a configuração
   foi ligada tarde, a data prometida passa a ser hoje + 15: nunca encerra sem aviso;
3. 3 dias antes, aviso final;
4. na data, encerra como acima (origem "Automático") e seguem os 90 dias do expurgo.

Assinou de novo: sai da lista. Sem administrador com e-mail: não é encerrada
(fica no log). A data prometida fica gravada no próprio e-mail de aviso
(tela E-mails, tipo "Aviso de encerramento").
