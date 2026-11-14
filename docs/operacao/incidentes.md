# Resposta a incidentes de segurança

O que fazer quando algo de segurança der errado no micoempresa: vazamento de
dados, conta invadida, certificado A1 exposto, servidor comprometido, senha ou
chave publicada por engano. Este documento é o roteiro: siga na ordem e anote
tudo no **registro do incidente** (modelo no fim).

> **Nenhuma senha neste documento.** Senhas e chaves ficam no gerenciador de
> senhas e em `/opt/nfsesaas/NfseSaaS/docker-compose.segredos.yml` na VPS.

## Quem é quem (LGPD)

| Dados | Papel do micoempresa | Quem avisa a ANPD e as pessoas |
|---|---|---|
| Contas e usuários do micoempresa (nome, e-mail, CPF/CNPJ da conta, WhatsApp, IP, cobrança) | **Controlador** | **Nós** |
| O que o cliente cadastra (tomadores, contratos, notas, documentos, certificado A1 da empresa dele) | **Operador** | **O cliente** (controlador). Nós avisamos o cliente logo, com tudo que ele precisa |

O responsável pelo micoempresa como controlador está em
`src/NfseSaaS.Domain/Legal/DocumentosLegais.cs` (`ResponsavelNome`), e o
canal de privacidade é `lgpd@micoempresa.com.br`.

## Prazos (Resolução CD/ANPD nº 15/2024)

- **ANPD:** em até **3 dias úteis** a partir do momento em que soubemos que o
  incidente atingiu dados pessoais, quando ele **pode trazer risco ou dano
  relevante** às pessoas. Agente de pequeno porte tem o prazo em dobro, mas
  **trabalhe com 3 dias úteis**.
- **Sem todas as informações:** mande a comunicação **preliminar** dentro do
  prazo e complemente em até **20 dias úteis**. Não espere terminar a
  investigação para comunicar.
- **Pessoas afetadas (titulares):** também em até 3 dias úteis, em linguagem
  simples, de preferência por e-mail individual.
- **Clientes (quando somos operador):** avise **no mesmo dia** em que
  confirmar, para eles cumprirem o prazo deles.
- **Registro:** todo incidente, comunicado ou não, fica registrado por pelo
  menos **5 anos**.

A comunicação à ANPD é feita pelo formulário de Comunicação de Incidente de
Segurança (CIS) no gov.br (procure "comunicar incidente de segurança ANPD").

## Passo 1 — Conter (primeira hora)

Objetivo: parar o estrago. Escolha só o que se aplica.

| Situação | O que fazer | Onde |
|---|---|---|
| Uma conta de cliente invadida | Plataforma › Contas › ⋯ › **Suspender** a conta; depois, com o cliente, **Resetar senha** de cada usuário | Sistema (PC) |
| Um usuário com senha vazada | Usuários › ⋯ › **Bloquear** e **Resetar senha** (pede a sua senha de novo) | Sistema (PC) |
| Senha do operador (você) exposta | Troque a senha; confira que a verificação em duas etapas está ligada; veja Auditoria e Logs | Sistema (PC) |
| Derrubar as sessões abertas de alguém | **Resetar senha** do usuário: a sessão dele cai em até 1 minuto (a revalidação do login confere o carimbo de segurança) | Sistema (PC) |
| Chave da API do Asaas ou token do webhook expostos | Gerar chave nova no painel do Asaas, trocar no arquivo de segredos da VPS e reiniciar o app | Asaas + VPS |
| Senha do banco, do SMTP ou da VPS expostas | Trocar a senha no serviço, no gerenciador de senhas e no arquivo de segredos da VPS; reiniciar | Hostinger + VPS |
| Certificado A1 de um cliente exposto | Avisar o cliente **na hora** para **revogar** o certificado na certificadora e comprar outro; enquanto isso, a empresa fica sem certificado no sistema | Cliente + certificadora |
| Servidor comprometido (processo estranho, arquivo alterado) | Tirar o site do ar (abaixo) e **não apagar nada**: as evidências estão na VPS | VPS |

### Tirar o site do ar na VPS (só se for preciso)

No PC, abra o PowerShell e entre na VPS:

```
ssh root@2.25.195.248
```

Já dentro da VPS:

```
cd /opt/nfsesaas/NfseSaaS
docker compose -f docker-compose.prod.yml -f docker-compose.segredos.yml stop app
```

O banco continua de pé (para investigação e backup). Para voltar:

```
docker compose -f docker-compose.prod.yml -f docker-compose.segredos.yml start app
```

## Passo 2 — Guardar as evidências (antes de corrigir)

Nada de apagar log ou "limpar" antes de copiar.

1. **Logs do sistema:** Plataforma › Logs (baixe os dias do incidente). Na VPS,
   ficam no volume `nfsesaas_app_logs`.
2. **Auditoria:** tela Auditoria da conta afetada (login bloqueado, troca de
   senha, envio de certificado, emissões e cancelamentos com a impressão
   digital do certificado, exportações).
3. **Nginx (VPS):** `/var/log/nginx/access.log` e `error.log` (IPs e horários).
4. **Backup do momento:** rode o backup manual (ver `backup-restauracao.md`)
   para ter a "foto" do estado.
5. Anote no registro: quando soube, como soube, o que viu, o que fez e a hora.

## Passo 3 — Avaliar

Responda no registro:

- **O que vazou ou foi alterado?** Quais tabelas/arquivos, quantas contas,
  quantas pessoas.
- **Que tipo de dado?** Dado de login, financeiro, documento, certificado A1,
  dado de criança ou dado sensível pesam mais.
- **Pode trazer risco ou dano relevante?** Ex.: alguém consegue entrar em
  contas, emitir nota em nome do cliente, aplicar golpe com os dados. Na
  dúvida, **trate como relevante e comunique**.
- **A causa foi encontrada e fechada?**

## Passo 4 — Comunicar

1. **Clientes afetados** (somos operador): e-mail no mesmo dia (modelo A).
2. **ANPD** (somos controlador e há risco relevante): formulário CIS em até 3
   dias úteis; preliminar se faltar informação.
3. **Pessoas afetadas** (somos controlador): e-mail individual (modelo B).
4. **Responder dúvidas** pelo `lgpd@micoempresa.com.br` e pelo suporte.

## Passo 5 — Corrigir e aprender

1. Corrija a causa, com teste automatizado que impeça a volta do problema.
2. Faça o deploy normal (GitHub Actions › Deploy Production).
3. Reative contas/usuários suspensos só depois de trocar senhas.
4. Escreva o resumo final no registro (o que aconteceu, impacto, o que mudou)
   e revise este documento se algo faltou.

## Registro do incidente (modelo)

Guarde num documento próprio, fora do repositório, por 5 anos.

```
Incidente nº ____        Aberto em: __/__/____ __:__   Fechado em: __/__/____
Como soubemos:
Descrição (o que aconteceu):
Dados atingidos (tipo, contas, quantidade de pessoas):
Somos controlador / operador / os dois:
Risco ou dano relevante? (sim/não e por quê):
Contenção (o que foi feito e quando):
Evidências guardadas (onde):
Comunicação aos clientes (quando, para quem):
Comunicação à ANPD (data, protocolo, preliminar/complementar):
Comunicação às pessoas (quando, como):
Causa e correção (commit, deploy):
Lições:
```

## Modelo A — aviso ao cliente (somos operador)

> **Assunto:** Aviso de segurança sobre a sua conta no micoempresa
>
> Olá, [nome].
>
> Em [data], identificamos [o que aconteceu, em uma frase simples]. Isso
> envolveu [quais dados da sua conta: ex. cadastro de tomadores, contratos,
> certificado digital].
>
> O que já fizemos: [contenção]. O que recomendamos que você faça agora:
> [ex.: trocar a senha; revogar o certificado A1 na certificadora].
>
> Como você é o responsável (controlador) pelos dados dos seus clientes, pode
> precisar comunicar a ANPD e as pessoas afetadas. Estamos à disposição para
> passar todas as informações de que precisar: responda este e-mail ou
> escreva para lgpd@micoempresa.com.br.
>
> [assinatura]

## Modelo B — aviso à pessoa afetada (somos controlador)

> **Assunto:** Aviso sobre os seus dados no micoempresa
>
> Olá, [nome].
>
> Em [data], ocorreu [o que aconteceu, em uma frase simples], que envolveu
> os seguintes dados seus: [lista]. [Possíveis consequências, sem exagero e
> sem esconder].
>
> O que já fizemos: [medidas]. O que você pode fazer: [ex.: trocar a senha,
> ligar a verificação em duas etapas, desconfiar de e-mails pedindo dados].
>
> Dúvidas: lgpd@micoempresa.com.br.
>
> [assinatura]

## O que o sistema já faz para ajudar

- Auditoria por conta (inclusive envio e troca do certificado A1 e qual
  certificado assinou cada nota e cancelamento).
- Logs sem senhas, tokens e CPF/CNPJ de respostas de terceiros (mascarados
  antes de gravar).
- Verificação em duas etapas obrigatória para o operador e os
  administradores; a conta pode exigir de todos.
- "Confirme a sua senha" antes de trocar certificado, permissões, ambiente,
  convidar ou exportar os dados.
- Alerta por e-mail ao operador em todo erro grave (ver `monitoramento.md`).
- Backup diário criptografado fora da VPS, com ensaio de restauração (ver
  `backup-restauracao.md`).
