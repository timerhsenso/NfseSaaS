# Contas (Plataforma › Contas)

Painel do operador da plataforma: todas as contas (clientes do micoempresa),
com números de uso, situação da cobrança e prêmios de indicação.

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
