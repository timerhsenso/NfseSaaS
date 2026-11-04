# Monitoramento (produção)

Três camadas, do mais grave ao mais específico:

| O quê | Como | Quem recebe |
|---|---|---|
| Site fora do ar | UptimeRobot consultando `https://micoempresa.com.br/health` a cada 5 minutos | e-mail cadastrado no UptimeRobot |
| Erro no sistema (log Error/Fatal) e job que falhou de vez | `AlertaErroSink` (Serilog) → `AlertaOperadorFila` → `AlertaOperadorHostedService` | operador(es) da plataforma (`Plataforma:UsuariosOperadoresIds`) |
| Reajuste de contrato aguardando aprovação ou aplicado automaticamente | job diário de reajustes → `AvisoReajustesAniversario` | administradores ativos da conta |

Todos os e-mails do sistema aparecem na tela **E-mails** de cada conta, com
status e reenvio. Os alertas do operador ficam na conta do próprio operador.

## UptimeRobot (configuração única)

1. Crie a conta gratuita em uptimerobot.com.
2. Crie um monitor com: **Monitor Type** `HTTP(s)`, **URL**
   `https://micoempresa.com.br/health`, **Interval** `5 minutes` e o seu
   e-mail em **Alert Contacts**.
3. O `/health` responde 200 com a aplicação e o PostgreSQL de pé. Responde
   503 se o banco cair, e o UptimeRobot também acusa esse caso.

## Alertas do operador

- **Entra:** todo log de nível Error ou Fatal. Isso inclui exceção não
  tratada em requisição (5xx), falha parcial de job (uma Empresa que deu
  erro) e falha definitiva de job do Hangfire (`AlertaFalhaJobFilter`, depois
  das novas tentativas do `[AutomaticRetry]`).
- **Não entra:**
  - respostas esperadas 4xx (regra de negócio, validação, não encontrado,
    sessão expirada), que são logadas como Warning;
  - erros do próprio envio de e-mail e do monitoramento, para não entrar em
    laço;
  - erros internos do Hangfire, porque a falha definitiva já chega pelo filtro.
- **Agrupamento:** mesmo erro (mesma origem, modelo de mensagem e tipo de
  exceção) gera no máximo 1 e-mail a cada 30 minutos; o próximo e-mail
  informa quantas repetições houve. Teto geral de 20 e-mails por hora.
- **Sem operador configurado:** nada é enviado. O log em arquivo continua
  completo (tela Plataforma › Logs).

## Aviso de reajustes

O job das 10:10 (Brasília) manda um e-mail por Empresa e por dia, só quando
gerou reajuste **aguardando aprovação** (pede ação na tela de Contratos) ou
**aplicado automaticamente** (informativo). "Aguardando índice" e "atrasado
demais" se repetem todo dia e não geram e-mail.
