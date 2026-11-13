# Changelog

Todas as mudanças relevantes do NfseSaaS são registradas aqui, por versão.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/);
versionamento segue [SemVer](https://semver.org/lang/pt-BR/) (MAJOR.MINOR.PATCH).

## [Não lançado]
### Melhorado (DOC3 — envio de documentos)
- **Data de emissão obrigatória** em todo documento novo (tela e servidor). Os já
  guardados sem data continuam valendo.
- **Validade por dias ou por data**: "Vale por (dias)" e "Válido até" andam
  juntos a partir da emissão — digitou os dias, a data final aparece; digitou a
  data, os dias aparecem; os dois em branco = documento sem validade (certidão
  sempre exige). Tipos com prazo usual já vêm com os dias (FGTS 30, federal e
  trabalhista 180). Resumo na hora: "Vale 30 dias, até 10/12/2026: faltam 27 dias".
- **"Outro documento" sugere os nomes já usados** na empresa, para não criar
  dois documentos iguais com grafias diferentes.
- **Envio com andamento visível** em todos os envios de arquivo (documentos da
  empresa, anexos do contrato, certificado A1 e logomarca): o botão mostra
  "Enviando 45%" com spinner e depois "Processando…"; nos documentos, também uma
  barra de progresso.
- **Mensagem certa quando o servidor recusa pelo tamanho** (ex.: limite do
  Nginx): antes dizia "até 5 MB" mesmo para arquivo menor.

### Adicionado (DB1 — painel financeiro)
- **"Seu negócio" no início**: o dono abre o sistema e vê o negócio, não só as
  notas. Conta só nota de **produção autorizada**, pela **competência**
  (cancelada, substituída, rejeitada e homologação ficam de fora).
- **Quatro indicadores**: faturado no mês (com o mês anterior), **receita
  recorrente** dos contratos mensais vigentes, **a emitir no mês** (contratos
  mensais sem nota autorizada, com as rejeitadas destacadas) e acumulado no ano.
- **Faturamento dos últimos 12 meses** em colunas, separando contratos de notas
  avulsas; dica com os valores ao passar o mouse, tocar ou navegar pelo teclado;
  "Ver em tabela" com todos os números.
- **Maiores clientes** (12 meses) com a fatia de cada um e aviso quando um só
  cliente responde por metade do faturamento.
- **Limite do regime** quando o painel mostra uma empresa: MEI pelo ano
  (R$ 81 mil, com os avisos de desenquadramento até 20% e retroativo) e faixa
  do Simples pelos 12 meses fechados (aviso acima de R$ 3,6 milhões).
- **Contratos sem nota no mês** com atalho que abre a Nota Mensal já na empresa
  e na competência certas.
- **Reajustes até o fim do mês que vem**, com atrasados e os que aguardam
  aprovação, valor novo **estimado pelo índice** do contrato (pelo último índice
  publicado quando o do mês ainda não saiu) e o impacto na receita recorrente.
- **Todas as empresas x esta empresa**: perfil Empresa soma todas; perfil
  Contador olha a empresa do topo. A escolha fica lembrada no navegador.
- Permissão: quem consulta Notas vê o faturamento; a parte de contratos exige
  também consultar Contratos. Sem nota de produção e sem contrato, o bloco não aparece.
- Sem mudança no banco. API: `GET /api/painel-financeiro`.

### Melhorado (UX2 — celular e tablet)
- Revisão de todas as telas em 390px (celular) e 768px (tablet), com conferência
  automática de "nada passa da largura da tela".
- **Listas em cartões no celular**: Notas, Contratos, Clientes, Serviços,
  Empresas, Usuários, Grupos, E-mails, Índices, Auditoria, notas recentes do
  painel e a Nota Mensal mostram cada linha como um cartão "rótulo: valor", com
  as ações embaixo — sem rolar a tabela de lado. No tablet e no computador,
  nada muda. (`.sem-cartoes` desliga numa tabela, se um dia precisar.)
- **Topo no celular**: o botão do menu não fica mais cortado e a página não
  "anda" de lado; o seletor de empresa usa o espaço que sobra.
- **Sem zoom do iPhone** ao tocar num campo (fonte de 16px nos campos no
  celular, respeitando a escala de fonte da Aparência).
- **Painel**: indicadores em duas colunas e mais baixos; avisos com o ícone na
  mesma linha do texto.
- **Botão principal primeiro** no topo das telas (Emitir nota, Novo contrato…).
- **Cadastros em seções** (Cliente, Empresa): no celular a aba aberta mostra o
  nome e as outras só o ícone — todas cabem na tela.
- Auditoria: a tabela larga rola dentro do quadro no tablet (antes estourava a página).

### Adicionado (DOC2 — documentos exigidos pelo cliente)
- **Cliente › Envio da nota › "Documentos que vão junto com a nota"**: marque as
  certidões e documentos que o cliente exige (federal, FGTS, CNDT, "Outros" da
  empresa…). Cada um mostra como está na empresa (válido, vencido, não enviado).
  Vai sempre a **versão que vale** em Documentos da empresa.
- **Envio automático** (logo depois da autorização): escolha no cliente o que
  fazer se um documento estiver vencido ou faltar — *mandar sem ele* (padrão),
  *mandar o vencido assim mesmo* ou *não mandar o e-mail* (eu envio depois). A
  nota é emitida de qualquer jeito; o aviso fica registrado.
- **Modal "Enviar nota por e-mail"**: lista de anexos (nota + documentos) com
  caixas para tirar um documento só naquele envio. Documento vencido marcado
  pergunta **"mandar assim mesmo?"** antes de enviar (o servidor também exige a
  resposta). Aviso quando os documentos passam de 10 MB.
- **Envio em lote / Nota Mensal**: aviso de quantas notas têm documento vencido
  ou faltando e a opção "Mandar os documentos vencidos assim mesmo" (sem marcar,
  vai sem o vencido).
- **E-mails enviados**: clipe com os documentos anexados e o alerta do envio. Os
  documentos são lidos na hora do envio (inclusive no "Reenviar"), sempre a
  versão escolhida ao enfileirar; arquivo sumido não derruba o e-mail da nota.
- Migration **DocumentosExigidosCliente**: `clientes.DocumentosExigidos`,
  `clientes.DocumentoPendenteEnvioAutomatico`, `email_logs.DocumentosAnexos`,
  `email_logs.AlertaDocumentos`.

### Adicionado (DOC1 — documentos da empresa)
- **Nova tela "Documentos da empresa"** (menu Cadastros): guarda em PDF as
  certidões e documentos da empresa selecionada no topo — certidão federal,
  estadual, municipal, FGTS (CRF), trabalhista (CNDT), Simples Nacional,
  contrato social, cartão CNPJ e **outros com nome livre** (ex.: alvará).
- Cada envio é uma **versão nova**: vale a mais recente, as anteriores ficam no
  **histórico** (baixar e excluir versão; excluindo a que vale, a anterior volta).
- **Validade**: obrigatória nas certidões; FGTS (30 dias), federal e CNDT (180
  dias) já vêm preenchidas pela data de emissão. Link do **site oficial** de
  cada órgão para tirar a certidão.
- Documento já vencido no envio: a tela avisa e pede "Guardar assim mesmo".
- **Avisos**: no **painel inicial** (vencidos e vencendo em até 15 dias, em
  todas as empresas) e por **e-mail** diário 08:25 (aos administradores e ao
  e-mail da empresa), uma vez quando faltam até 15 dias e outra quando vence.
  Enviou a versão nova, os avisos param. Novo tipo "Documento vencendo" em
  E-mails enviados.
- Permissão: a mesma de Empresas (Consultar vê e baixa; Alterar envia e exclui).
- Planos (PL1): novo recurso **"Documentos da empresa"** (padrão: liberado).
  Bloqueado: só vê e baixa o que já foi enviado.
- Arquivos em disco na mesma raiz dos documentos de contrato
  (`DocumentStorage:BasePath/{conta}/{empresa}/documentos/`); entram na
  **exportação dos dados** (`empresas_documentos`) e somem na exclusão da
  empresa, no encerramento e na exclusão da conta.
- Migration **DocumentosEmpresa**: tabela `documentos_empresa`.

### Adicionado (PL1 — planos flexíveis)
- **Plataforma › Planos › "Recursos e limites"**: o operador monta cada plano
  pela tela, sem código e sem deploy. Cada recurso fica em **Padrão** (como o
  sistema funcionava antes), **Liberado/Bloqueado** ou **Ilimitado/Até N**.
  Recursos de hoje: **Logomarca própria**, **E-mail da nota sem o convite do
  micoempresa**, **Usuários na conta** e **Clientes por empresa**.
- **Plataforma › Contas › "Recursos e exceções…"**: mostra o que vale para a
  conta e de onde vem (padrão, plano ou exceção) e deixa criar **exceção por
  conta** (cortesia, negociação, teste de recurso), com motivo obrigatório e
  validade opcional. A exceção vence o plano enquanto valer; tudo vai para o
  histórico da conta.
- Ponto único de decisão no código (`IRecursosConta`): exceção → plano da
  assinatura → padrão do catálogo. Recurso novo entra no catálogo
  (`CatalogoRecursos`) com o padrão = como funcionava antes.
- Limites só barram **cadastro novo** (convidar usuário, cadastrar cliente),
  com mensagem dizendo o que fazer. **A emissão de nota nunca é bloqueada** e o
  que já existe continua funcionando.
- Recurso fechado aparece com **cadeado** e "conhecer os planos" (logomarca em
  Configurações da conta; rodapé do e-mail em Modelos de e-mail).

### Alterado (PL1)
- Logomarca e rodapé do e-mail deixam de ser fixos por perfil no código: viram
  recursos do plano. Sem configurar (e com a cobrança desligada), continuam
  como antes: liberados só para contas de contador.
- Banco: tabelas `planos_recursos` e `excecoes_recurso_conta` (migration
  `RecursosPlano`).

### Alterado (cadastro de cliente em seções)
- **Cliente** com o mesmo layout do cadastro de Empresa: seções à esquerda
  (**Dados gerais**, **Endereço**, **Envio da nota**, **Retenções**) com ✓ / !
  pelo preenchimento, em vez de uma tela comprida. Telefone foi para Dados
  gerais; e-mail, envio, cópias e modelo do e-mail ficam juntos em Envio da
  nota; ISS e tributos federais em Retenções.
- Ao salvar com campo faltando, a tela abre a seção do campo e diz qual é;
  erro do servidor sobre endereço, e-mail ou retenção abre a seção certa.
- Mensagem "Preencha … antes de salvar" passa a usar o rótulo do campo
  também em campos com botão ao lado (CEP, CNPJ) — vale para todas as telas.

### Adicionado (C6 — descontos e retenções federais)
- **Emitir nota › "Descontos e retenções"** (fechado por padrão; abre sozinho
  quando o cliente tem retenção): desconto **incondicionado** (reduz o valor e
  a base) e **condicionado** (informativo, não reduz a base); retenções de
  **IRRF, PIS, COFINS, CSLL e INSS**, cada uma com o percentual (padrões 1,5 /
  0,65 / 3 / 1 / 11). O **valor líquido** aparece na hora, com a conta linha a
  linha (cálculo no servidor, `POST /api/nfse/retencoes/simular`).
- **Cliente › Fiscal › Retenções federais que este cliente faz**: quem retém é
  o tomador. A Nota Mensal e a automação aplicam sozinhas, **sem o que o regime
  da empresa dispensa** (Simples: só INSS; MEI: nada).
- **Avisos por regime** (não bloqueiam): no Simples, marcar IRRF ou
  PIS/COFINS/CSLL avisa da dispensa (IN RFB 765/2007 e IN SRF 459/2004); INSS
  lembra "só Anexo IV com cessão de mão de obra"; MEI não mostra retenções. Os
  avisos aparecem de novo no resumo antes de emitir: o usuário decide.
- **Dispensa de R$ 10**: IRRF até R$ 10, PIS+COFINS+CSLL até R$ 10 e INSS
  abaixo de R$ 10 saem zerados, com aviso; "Reter mesmo até R$ 10" para o
  tomador que soma as notas do mês.
- **Empresa › Regime tributário › "Avisar na nota que o Simples é dispensado de
  retenção"**: acrescenta a frase da dispensa no fim da descrição das notas sem
  retenção federal (não repete, não passa de 2.000 caracteres).
- Líquido zero ou negativo é barrado antes de gastar o número da DPS.
- Detalhe da nota mostra descontos e retenções; resumo da emissão mostra
  descontos, retenções e líquido.
- Modelos de e-mail: novo campo **[Valor líquido]**; o quadro fixo do e-mail
  mostra o líquido quando é diferente do valor do serviço.

### Alterado (C6)
- **tpRetPisCofins passa a ser calculado em cada nota** pelo que foi retido
  (tabela oficial 0, 3 a 9 — NT 007) e sai do cadastro da Empresa (o campo
  "Retenção PIS/COFINS" foi removido; escolher "retido" ali sem valor gerava
  rejeição E0724).
- DPS: `vDescCondIncond` (vDescIncond, vDescCond) e, em `tribFed`, `vRetCP`
  (INSS), `vRetIRRF` e `vRetCSLL` com **PIS + COFINS + CSLL somados**, como
  manda a NT 007 (vPis/vCofins continuam sem uso: são débito próprio). Validação
  local de E0720/E0724 antes de enviar.
- Banco: `notas_fiscais` ganha DescontoIncondicionado, DescontoCondicionado,
  ValorRetidoIrrf, ValorRetidoPis, ValorRetidoCofins, ValorRetidoCsll,
  ValorRetidoInss; `clientes` ganha os 5 percentuais de retenção; `empresas`
  ganha AvisoDispensaRetencaoNaDescricao e perde TpRetPisCofins (migration
  `RetencoesFederais`). Exportação de dados acompanha.

### Alterado (modelos de e-mail reutilizáveis)
- **Novo cadastro "Modelos de e-mail"** (menu Cadastros e referência): o texto
  do e-mail da nota deixa de ser uma configuração da conta e vira modelos com
  nome. Lista à esquerda (quem usa cada um), editor com os botões de campo e
  prévia à direita. Crie quantos quiser (até 30), **duplique** para aproveitar
  o texto de outro, marque um como **padrão da conta** (estrela) e exclua os
  que não usa (quem usava volta ao padrão; o padrão não pode ser excluído).
  Toda conta ganha o modelo "Padrão" com o texto do sistema.
- **Empresa › Dados gerais › Modelo do e-mail da nota**: "Padrão da conta" ou
  um modelo. Várias empresas podem usar o mesmo modelo.
- **Cliente › Modelo do e-mail da nota** (opcional): para o cliente que
  precisa de texto próprio. Ordem no envio: cliente → empresa → padrão da conta.
- **Enviar por e-mail (uma nota)**: o modal mostra o modelo usado e deixa
  **trocar o modelo** ali (texto, respostas e cópia oculta vêm do escolhido),
  além de ajustar o texto só naquele envio.
- Respostas e cópia oculta passam a ser de cada modelo; tirar o convite do
  rodapé também (plano Escritório).
- Configurações da conta › E-mail da nota vira só um atalho para Modelos de e-mail.
- API: `GET/POST /api/modelos-email-nota`, `GET /api/modelos-email-nota/opcoes`,
  `PUT/DELETE /api/modelos-email-nota/{id}`, `POST .../{id}/duplicar`,
  `POST .../{id}/padrao`, `POST /api/modelos-email-nota/teste`,
  `PUT /api/empresas/{id}/modelo-email-nota`, `PUT /api/clientes/{id}/modelo-email-nota`;
  `GET /api/nfse/{id}/email-previa?modeloId=`; `POST /api/nfse/{id}/enviar-email`
  aceita `modeloId`. Saíram `GET/PUT /api/conta/email-nota` e `POST /api/conta/email-nota/teste`.
- Banco: `modelos_email_nota` ganha `Nome` (único na conta) e `Padrao` (um por
  conta) e perde `EmpresaId`; `empresas` e `clientes` ganham `ModeloEmailNotaId`
  (FK, excluir o modelo zera). A migration converte o que existir: o modelo
  da conta vira "Padrão"; o de cada empresa vira um modelo com o nome dela,
  já escolhido na empresa.

### Adicionado (e-mail da nota: texto próprio e envio em lote)
- **Configurações da conta › E-mail da nota** (nova seção): assunto e mensagem
  do e-mail que leva a nota (PDF e XML) ao cliente, com **botões que inserem
  os campos** onde está o cursor — [Cliente], [Número da nota], [Mês por
  extenso] ("novembro de 2026"), [Competência], [Valor], [Data de emissão],
  [Serviço], [Nº do contrato] e [Empresa] — e **prévia ao vivo** ao lado.
  Campo escrito à mão que não existe é avisado. "Mandar um teste para mim"
  (dados de exemplo, sem anexos) e "Voltar ao texto padrão".
- **Modelo da conta ou por empresa**: com mais de uma empresa, escolhe-se
  "Modelo de" e liga "Personalizar para esta empresa"; desligado, a empresa
  usa o da conta. Só o administrador grava.
- **Respostas do cliente** vão para o e-mail da empresa emitente (ou outro
  informado). Saiu o "não responda". **Cópia oculta de todo envio**
  (opcional), por exemplo o escritório que guarda as notas.
- Plano Escritório (contador) pode **tirar o convite do micoempresa** do rodapé.
- O que dá validade à nota continua fixo embaixo da mensagem: quadro com
  número, emissão, competência, valor, emitente e chave, consulta pública e
  os anexos. Mensagem é texto simples (escapado, sem HTML).
- **Notas › Enviar por e-mail** (uma nota): abre com para quem vai (cópias,
  cópia oculta, respostas), o assunto e a mensagem já preenchidos, ajustáveis
  só para aquele envio, e avisa se a nota já foi enviada.
- **Envio em lote**: na barra de seleção da lista, "Enviar por e-mail" mostra
  o resumo (quantas notas e clientes, sem e-mail, não autorizadas, já
  enviadas com "Pular as que já foram enviadas" marcado) e o resultado. Um
  e-mail por nota, com o modelo da empresa de cada nota. Até 200 por vez.
- **Envelope na lista**: ao lado do cliente, enviado (verde), na fila (cinza)
  ou falhou (vermelho), com a data na dica.
- **Nota Mensal**: depois de emitir, aviso "N notas emitidas ainda não foram
  por e-mail" com "Enviar por e-mail agora" (o envio automático continua só
  para cliente com "enviar por e-mail" ligado).
- API: `GET/PUT /api/conta/email-nota`, `POST /api/conta/email-nota/teste`,
  `GET /api/nfse/{id}/email-previa`, `POST /api/nfse/enviar-email/lote/previa`,
  `POST /api/nfse/enviar-email/lote`; `POST /api/nfse/{id}/enviar-email`
  aceita `{ assunto, mensagem }` opcional.
- Banco: tabela nova `modelos_email_nota` (um da conta e no máximo um por
  empresa; sai com a empresa e na exclusão/encerramento da conta) e coluna
  `CopiaOcultaFixa` em `email_logs`.

### Adicionado (desfazer e substituir reajuste)
- **Contratos › Histórico de valores › Desfazer**: o último reajuste do
  contrato pode ser desfeito com motivo obrigatório (mínimo 10 caracteres).
  Valor, serviços e data do último reajuste (aniversário) voltam ao que eram.
  O evento continua no histórico, riscado, com "Desfeito em … por …: motivo",
  e a operação vai para a Auditoria ("DesfazerReajusteContrato").
- Quando **não** pode desfazer, o clique explica o motivo: já saiu nota de
  **produção** com o valor novo (homologação não conta), veio outro evento
  depois (só o último pode ser desfeito), há reajuste aguardando aprovação ou
  o valor do contrato mudou. Alteração de escopo não se desfaz (corrige-se
  editando os serviços).
- **Aviso de dois reajustes no mesmo ciclo**: ao registrar um reajuste com
  outro aplicado há menos de uma periodicidade (12 meses, no comum), a tela
  pergunta antes de gravar:
  - **Substituir o reajuste de dd/mm** (só se ele puder ser desfeito): desfaz
    o anterior e aplica o mesmo % sobre o valor de antes dele, na mesma data,
    tudo numa gravação só. Ex.: R$ 328,98 × 4,6284% = R$ 344,21.
  - **Somar mais um reajuste**: aplica sobre o valor atual, como antes.
  - O servidor também exige a escolha (`SubstituirReajusteId` ou
    `ConfirmarReajusteRecente`), para nenhum caminho somar sem querer.
- Exportação de dados leva `DesfeitoEm` e `MotivoDesfazer` do histórico de valores.
- API: `POST /api/contratos/{id}/reajustes/{movimentacaoId}/desfazer` (`{ motivo }`).
- Banco: `StatusMovimentacaoValor.Desfeito = 4` e 4 colunas novas em
  `movimentacoes_valor_contrato` (`DataUltimoReajusteAnterior`, `DesfeitoEm`,
  `DesfeitoPorUsuarioId`, `MotivoDesfazer`), todas opcionais, sem índice.

### Adicionado (WhatsApp da conta)
- **Cadastro**: campo **WhatsApp (opcional)** na etapa 3, com máscara
  "(71) 99999-8888". Preenchido, aparece a caixinha "Aceito receber contato e
  avisos do micoempresa por WhatsApp" (desmarcada; LGPD). Número inválido é
  recusado com mensagem clara; em branco, o cadastro segue igual.
- **Configurações da conta › Dados da conta** (nova seção, acima da
  Logomarca): titular, CPF/CNPJ e e-mail do responsável (só leitura) e o
  **WhatsApp do responsável** com a autorização. Só o administrador muda; os
  demais veem só para leitura.
- **Painel**: lembrete para o administrador enquanto a conta não tem
  WhatsApp ("Informar agora" / "Agora não"); "Agora não" adia 30 dias, no
  servidor (vale em qualquer navegador).
- **Plataforma › Contas**: WhatsApp na coluna Responsável, com link que abre
  a conversa (wa.me) já com "Olá, aqui é do micoempresa…"; "(só contato)"
  quando a conta não autorizou avisos.
- Exportação de dados da conta leva WhatsApp e data da autorização; o
  expurgo da conta encerrada apaga os dois.
- Regras: celular do Brasil (DDD válido, 9 dígitos começando por 9),
  guardado como 55+DDD+número (13 dígitos) — o formato que links e APIs de
  envio usam. A data da autorização é guardada e mantida ao trocar o número;
  apagar o número apaga a autorização. Alteração vai para a Auditoria
  ("AlterarContatoConta").
- Banco: 3 colunas novas em `tenants` (`WhatsApp`, `WhatsAppAutorizadoEm`,
  `LembreteWhatsAppAdiadoAte`), todas opcionais.

### Alterado (cores de volta ao painel Aparência; Configurações da conta só com a Logomarca)
- Pedido do Carlos: as **cores da conta voltaram ao painel Aparência**, como
  antes (clique muda a tela na hora, "Salvar" grava). O painel volta a ser o
  que era antes do B9.
- **Configurações da conta** ficou só com a seção **Logomarca** (enviar e
  remover). Mensagens passaram de "logo" para "logomarca".

### Adicionado (B9 — logo do escritório e Configurações da conta)
- **Configurações da conta** (menu do usuário, administrador): página do
  que vale para a conta inteira. Primeira seção, **Identidade visual**:
  - **Logo da conta** (plano Escritório = contas de contador; o operador da
    plataforma também pode, para conferir): topo do menu, sobre uma placa
    clara, com "com micoempresa" pequeno embaixo, para todos os usuários da
    conta, e no alto das impressões das listas. Enviar e remover valem na hora.
  - **Cores da conta** (saíram do painel Aparência): clique pré-visualiza,
    "Salvar cores" grava para todos.
- O painel **Aparência** ficou só com as preferências de cada pessoa (modo,
  texto, botões, menu), com um atalho do administrador para as Configurações.
- Logo: PNG, JPG ou WebP até 512 KB, reconhecido pelo conteúdo (SVG recusado:
  pode levar script). Guardado no banco (tabela `logos_conta`, migration
  `LogoConta`), um por conta; a URL leva a versão (hash), o navegador guarda
  em cache e nunca mostra o antigo. Auditoria AlterarLogoConta/RemoverLogoConta.
  Some na exclusão e no expurgo da conta.

### Alterado (largura das colunas nas listas)
- Colunas com valor curto (códigos, CPF/CNPJ, valores, datas, status,
  ações) ficam do tamanho do conteúdo e não quebram; a sobra vai para os
  textos (descrição, nome, cliente). Códigos alinhados à esquerda, valores à
  direita com dígitos alinhados. Vale para todas as listas (classes
  `col-curta`, `col-codigo`, `col-valor`; `autoWidth` do DataTables desligado).

### Alterado (telas de lista mais limpas — todas as telas)
- **Cabeçalho da página com as ações**: título à esquerda; à direita
  **Exportar ▾**, **⋯** (ações raras) e a ação principal. Sai o título
  repetido dos cartões ("Notas fiscais emitidas", "Clientes cadastrados"…).
- **Exportar ▾** num menu só (Excel, CSV, Copiar, Imprimir), em todas as
  listas; em Notas, o **XML do mês** entrou nesse menu. Rodapé "exportado
  por" e registro na auditoria continuam iguais.
- **Uma barra acima da tabela**: pesquisa + filtros da tela. "Exibir N",
  "Mostrando…" e paginação ficaram embaixo da tabela.
- **Ações das linhas**: até 2 botões à vista + **⋯** com o resto; Excluir
  sempre no ⋯ (evita clique sem querer). Clientes, Serviços, Empresas,
  Contratos, Usuários, Grupos, Planos, Contas da plataforma e Notas.
- **Notas Fiscais**: período em atalhos (Este mês, Mês passado, Últimos 30
  dias, Este ano, Personalizado — as datas só aparecem no personalizado);
  "Limpar filtros" só com filtro ativo; com notas marcadas aparece a barra
  "N selecionadas · R$ total · Baixar PDFs (ZIP)"; linha mostra PDF + ⋯
  (Detalhes, XML, Enviar por e-mail, Substituir); clicar na linha abre os
  Detalhes; valor líquido só aparece (embaixo do valor) quando é diferente;
  emissão com a hora menor embaixo. "Buscar notas da SEFIN" foi para o ⋯.
- Contratos: "Verificar reajustes agora" no ⋯. Índices: "Importar do Banco
  Central agora" no ⋯. Usuários: o formulário de convite abre pelo botão
  "Convidar usuário". Plataforma: "Jobs (Hangfire)" no ⋯.

### Adicionado (número do contrato e totais da lista)
- **Nº do contrato** (texto livre, até 30 caracteres, único por empresa sem
  diferenciar maiúsculas). Ao abrir "Novo contrato" já vem o próximo
  "ano/NNNN" da empresa (ex.: 2026/0001), e dá para trocar pela numeração
  própria. Rota `GET api/contratos/proximo-numero`. Migration `NumeroContrato`.
- Contratos que já existem ganham número ao subir o sistema
  (`NumeroContratoBackfill`): por empresa, na ordem da data de início, com o
  ano do início (2019/0001…). Idempotente.
- **Descrição do contrato agora é opcional.** A coluna "Descrição" da lista
  virou **Nº** (com a descrição pequena embaixo, quando houver) e a pesquisa
  acha pelo número. Nota Mensal, e-mails da automação, reajustes, Histórico
  de valores e o combo de contrato na emissão mostram "Contrato 2026/0007".
  A exportação de dados ganhou a coluna Numero.
- **Rodapé da lista de Contratos**: receita mensal ativa (Ativos com cobrança
  Mensal, R$/mês) e total da lista, respeitando a pesquisa.

### Corrigido (erro ao salvar contrato cadastrado com R$ 1)
- Corrigir o valor de um contrato cadastrado com valor simbólico (R$ 1 →
  R$ 2.000) dava erro 500 ("numeric field overflow"): a variação de
  +199.900% não cabe na coluna do percentual do Histórico de valores.
  Regra única `PercentualVariacao`: variação fora do que a coluna guarda
  (±99.999,9999%) fica sem percentual ("—"), com os valores anterior e novo
  registrados. Vale para editar o contrato, registrar reajuste, aprovar
  reajuste e "gravar no contrato" da Nota Mensal.

### Adicionado (simular reajuste pelo índice)
- **Histórico de valores › Registrar novo reajuste** calcula sozinho, ao
  abrir e a cada troca de data ou índice, o **acumulado de 12 meses** do
  índice (a mesma conta do reajuste automático do aniversário) e mostra
  "valor atual → valor novo", com os 12 meses para conferir. **Usar este
  reajuste** preenche valor, % e observação; a pessoa confere e registra.
  Rota `GET api/contratos/{id}/reajustes/simulacao` (não grava nada).
- Data proposta: o **aniversário do contrato** quando vencido ou a até 45
  dias; senão hoje (link para voltar ao aniversário).
- Acumulado negativo segue a regra do contrato (mantém ou reduz), com a
  opção de inverter na hora. Índice "Outro", sem índice, mês faltando ou
  tabela parada: o quadro explica e aponta para Índices econômicos.

### Alterado
- **Reajuste vale só para a parte fixa do contrato**: serviço de valor
  variável (ex.: HelpDesk) fica com o valor de referência e o % é calculado
  sobre os fixos — no reajuste manual, no automático do aniversário e na
  aprovação. Contrato só com serviços variáveis reajusta tudo, como antes.

### Corrigido (homologação é homologação, produção é produção)
- **Nota Mensal** separa os ambientes: nota de teste em homologação não marca
  o mês de produção como "Já emitido", não vira "última nota" nem entra no
  "Repetir últimos valores"; o mesmo vale ao voltar para homologação.
- **Trava contra nota repetida por ambiente** (migration
  `NotaIdempotenciaPorAmbiente`: o índice único da chave de idempotência
  passa a incluir o ambiente). Antes, o lote em produção devolvia a nota de
  homologação do mesmo mês como "Emitida" sem mandar nada para a SEFIN.
- Reenvio de nota rejeitada só no mesmo ambiente em que ela nasceu.
- Automação da Nota Mensal confere o mês anterior só no ambiente atual.
- Indique e ganhe ("já emitiu nota") e Plataforma › Contas contam só notas de
  produção.

### Corrigido (número da NFS-e enorme na lista)
- Algumas notas mostravam no **Número** o Id da nota ("NFS" + chave de 50
  dígitos) em vez do número oficial curto (ex.: 44). Ao subir, o sistema
  corrige sozinho (`NumeroNfseCorrecao`, idempotente, sem migration): usa o
  nNFSe do XML; sem XML, o número que está dentro da chave de acesso.
- Regra única `NumeroNfseOficial` em todos os pontos que gravam o número
  (emissão e busca na SEFIN): nunca guarda Id; se o XML vier sem nNFSe, o
  número é tirado da chave de acesso.

### Alterado (Nota Mensal: repetir últimos valores)
- **Repetir últimos valores** vale para todos os serviços (fixos e variáveis):
  cada um recebe o valor que teve na última nota; fixo que mudou mostra
  "gravar no contrato". A dica "última nota: R$ X" também aparece no fixo.
- Contrato com vários serviços cuja última nota é anterior à separação por
  serviço mostra o **total da última nota** na linha para dividir; o aviso
  do botão diz quantos ficaram assim.

### Adicionado (Nota Mensal com valor por serviço)
- Contrato: cada serviço pode ser marcado como **valor variável** (muda a
  cada nota, ex.: horas de suporte). Mudar só essa marcação não vira evento
  do Histórico de valores.
- Nota Mensal: **um campo de valor por serviço** (com o total da linha).
  Serviço variável vem com o **último valor emitido** e a dica "último: R$ X
  (mm/aaaa)" para repetir com um clique; serviço fixo vem do contrato e, se
  alterado, oferece **"gravar no contrato"** (vira Alteração no Histórico de
  valores, vigência no 1º dia da competência; obrigatório quando o contrato
  não permite valor diferente só na nota; só para quem pode alterar contratos).
- Botões **Repetir últimos valores** e **Gravar alterações nos contratos**, e o
  total das notas selecionadas.
- A nota guarda o valor de cada serviço (`ComposicaoValoresJson`), mostrado no
  detalhe; reenvio e substituição com o mesmo total herdam a composição.
- Automação (modo automático): contrato com serviço variável não é emitido
  sozinho; entra no e-mail como "aguardando o valor".
- Migration `ValorVariavelNotaMensal`.

### Adicionado (substituição de nota, C7)
- Notas › botão **Substituir** (nota autorizada): abre a emissão com os dados
  da nota, para corrigir e emitir a substituta com o motivo oficial (cMotivo
  01–05 ou 99 com descrição de 15 a 255 caracteres). A DPS leva o grupo
  `subst` com a chave da original; autorizada a substituta, a SEFIN cancela a
  original (evento 105102) e ela fica "Substituída".
- Campos que a SEFIN não deixa mudar ficam travados e são conferidos antes de
  gastar o número da DPS: Simples (MEI e ME/EPP) — cliente, competência e
  valor (E0061/E0063); não optante — competência, serviço e local (E0060).
  Cliente, contrato e competência ficam sempre travados na tela.
- Mesma permissão do cancelamento (rota `POST api/nfse/{id}/substituir`); só
  uma substituta em andamento ou autorizada por nota (índice único parcial);
  substituta rejeitada pode ser reenviada mantendo a substituição.
- Detalhe da nota mostra "Substitui" e "Substituída por". Migration
  `SubstituicaoNfse` (3 colunas e FK em `notas_fiscais`).

### Adicionado (local da prestação por nota, C2)
- Emissão: campo **Local da prestação** (busca do município pelo nome). Em
  branco, vai o município da empresa, como sempre. Aparece no resumo antes de
  emitir e no detalhe da nota; o reenvio de nota rejeitada traz o da tentativa
  anterior.
- Contrato: **local da prestação padrão** das notas (serviço feito no cliente,
  em outro município). A Nota Mensal e a automação usam o do contrato; na nota
  avulsa dá para trocar ou voltar para o da empresa.
- O código precisa existir na tabela do IBGE (SEFIN E0302), conferido antes de
  gastar o número da DPS. A nota guarda o `cLocPrestacao` enviado.
- Migration `LocalPrestacao` (coluna opcional em `notas_fiscais` e em `contratos`).

### Adicionado (reajuste de quem já assina, B2)
- Plataforma › Planos › **Reajustar** (planos com assinantes): leva quem já
  assina ao preço de tabela do plano numa data escolhida, com **prévia** de
  quem entra e por que os outros ficam de fora. Regras dos Termos 1.1: aviso
  com pelo menos 30 dias, no máximo um reajuste a cada 12 meses por
  assinatura (desde que assinou ou do último reajuste), preço de fundador fora.
- Aviso por e-mail na hora do agendamento (tipo "Reajuste da assinatura"), só
  para administradores com e-mail confirmado. Tela Assinatura mostra "a partir
  de dd/mm: R$ X/mês".
- Job `aplicar-reajustes-assinatura` (07:20): na data, altera o valor no Asaas
  e na assinatura. Troca de plano, cancelamento ou valor diferente antes da
  data cancelam o reajuste; o operador pode cancelar o lote. Falha no Asaas
  repete no dia seguinte (5 vezes, depois "Erro" e alerta de job).
- Tabela `reajustes_assinatura`. Migration `ReajusteAssinatura`.

### Adicionado (cliente compartilhado entre as empresas da conta, C10)
- Cadastro do cliente com **"Disponível para"**: "Só esta empresa" ou
  "Todas as empresas da conta" (`clientes.EmpresaId` nulo = compartilhado).
  Mudou o e-mail no compartilhado, muda para todas as empresas. Contas
  diferentes continuam isoladas. Migration `ClienteCompartilhado`.
- A lista de uma empresa mostra os exclusivos dela e os compartilhados; se a
  empresa tem um exclusivo com o mesmo CPF/CNPJ de um compartilhado, só o
  exclusivo aparece ali (o exclusivo vence). Vale para a tela Clientes, a
  emissão, os contratos, a busca de notas na SEFIN e a nota da mensalidade.
- Padrão do campo: perfil Empresa → todas; Contador → só esta; conta com uma
  empresa → campo oculto (só esta). Coluna "Disponível para" na lista
  (oculta com uma empresa); `GET api/clientes/disponibilidade`.
- Proteções: compartilhado só vira exclusivo se não tiver nota nem contrato
  em outra empresa; um compartilhado por documento na conta; apelido único
  na lista de cada empresa. Índices únicos de CPF/CNPJ e apelido viram
  parciais (um para compartilhados, outro para exclusivos). Notas emitidas
  não mudam (guardam o tomador da época). Exportação: coluna EmpresaCnpj =
  "Todas" para compartilhados. Clientes atuais ficam como estão (exclusivos).

### Adicionado (municípios em tabela local, C9)
- Tabela `municipios` com os 5.571 municípios do IBGE (código, nome, UF),
  carregada no startup a partir de um arquivo embarcado (inclui os que
  faltam e atualiza nomes; nunca apaga). Migration `Municipios`.
- Telas de Cliente e de Empresa: além do CEP, **busca do município pelo
  nome** ("salvador", "são paulo/sp" ou o código), que preenche o código
  IBGE e a UF. O nome ao lado do código vem da tabela (não depende mais da
  API do IBGE no ar).
- Ao salvar Cliente ou Empresa, o código do município precisa existir (a
  SEFIN rejeitaria a nota, E0238) e a UF passa a ser a do município.

### Adicionado (verificação em duas etapas, B3)
- Opcional, por usuário: menu do usuário › **Verificação em duas etapas**.
  Ligada, o login pede, depois da senha, um código de 6 dígitos enviado ao
  e-mail (direto pelo SMTP, fora do log de e-mails). Para ligar, um código
  chega ao e-mail e é digitado (o e-mail passa a contar como confirmado);
  para desligar, a senha. Opção "não pedir de novo neste navegador" por 14
  dias. Código errado conta como tentativa de login (5 erradas bloqueiam por
  15 minutos). Aviso por e-mail ao ligar e ao desligar; registro na
  auditoria; selo "2 etapas" na tela Usuários. Usa o 2FA do próprio
  Identity: sem migration.
- O e-mail do código tem prazo de 20 segundos para sair: SMTP travado vira
  "não conseguimos enviar, tente de novo" em vez de a tela de login parar.
- Testes de integração não mandam mais e-mail de verdade (o SMTP dos
  user-secrets do PC travava a suíte por 2 minutos).

### Alterado (endereço do tomador, C3)
- Cliente pessoa física (CPF) sem retenção de ISS pode ficar **sem endereço**:
  a DPS vai sem o grupo `end`. Para CNPJ e para quem retém o ISS o endereço
  continua obrigatório (exigência da SEFIN, E0235/E0237), e também na nota
  com IBS/CBS (E0239) — a emissão para antes de gastar o número da DPS e diz
  onde completar. Endereço pela metade não é aceito (tudo ou nada).
- A DPS passa a levar o **complemento** (`xCpl`), o **telefone** (`fone`, só
  dígitos, 6 a 20) e o **e-mail** (`email`, até 80 caracteres) do cliente;
  fora do formato, o campo é omitido em vez de rejeitar a nota.
- Tela Clientes: os campos do endereço só ficam obrigatórios para CNPJ, para
  quem retém o ISS ou quando algum deles foi preenchido. Sem migration.

### Adicionado (NFS-e da mensalidade, N1 — DESLIGADO)
- Cada mensalidade paga no Asaas vira uma NFS-e emitida pela LFMS (empresa da
  conta do operador) para a conta que pagou: o webhook agenda o job
  `emitir-notas-mensalidade`, que também roda todo dia às 08:10. Um pagamento,
  uma nota (chave `mensalidade:<pagamento>`); tomador criado sozinho com o
  endereço da empresa da conta; até 8 tentativas com alerta ao operador;
  estorno antes da nota não emite, depois avisa para cancelar.
- O item da LC 116, o NBS e o IBS/CBS vêm do Serviço indicado na configuração
  (sugestão 01.05 / cIndOp 100501 / cClassTrib 000001): trocar é editar o Serviço.
- Liga com `NotaMensalidade__EmpresaId` e `NotaMensalidade__ServicoId` na VPS
  (opcional `NotaMensalidade__EmitirAPartirDe`). Tabela nova `notas_mensalidade`
  (migration). Detalhes em `docs/operacao/cobranca.md`.

### Adicionado (encerramento automático, D2 parte 2 — DESLIGADO)
- Job diário `encerrar-contas-canceladas` (05:00): com
  `Encerramento:AutomaticoAposCancelarDias` preenchido (mínimo 30; vazio =
  desligado, o padrão), conta com a assinatura cancelada recebe aviso por
  e-mail 15 dias antes e 3 dias antes (com os links para exportar os dados e
  assinar de novo) e, na data, é encerrada como no encerramento a pedido
  (origem "Automático"; expurgo 90 dias depois). Nunca encerra sem aviso nem
  sem destinatário; fora: isentas, operadores, suspensas e contas com a
  cobrança desligada. Só ligar com Termos novos (os 1.1 prometem a conta
  disponível depois de cancelar). Detalhes em `docs/operacao/contas.md`.

### Adicionado
- **XML do mês** (tela de Notas fiscais): um ZIP com o XML oficial de todas as
  notas autorizadas e canceladas da empresa no mês de competência, para o
  contador. Na hora, sem PDF e sem limite de vezes (até 5.000 notas por arquivo).

### Alterado (exportação dos dados)
- Uma exportação completa a cada 7 dias por conta: enquanto a última estiver
  pronta, a tela oferece baixar essa em vez de montar outra. Se falhou ou o
  arquivo sumiu, pode pedir de novo na hora. Continua o limite de 3 pedidos por dia.
- O operador libera uma nova antes do prazo em Plataforma › Contas
  (menu da conta › "Liberar nova exportação dos dados").
- Teto de espaço para as exportações prontas de todas as contas
  (`ExportacaoDados:LimiteTotalMb`, padrão 2048 MB): passou, o pedido é recusado
  com "tente de novo amanhã" e vai um alerta para o log.

### Adicionado (LGPD — encerramento da conta, D2 parte 1)
- **Encerrar a conta** pela tela "Meus dados" (administrador, com código de 6
  números por e-mail) ou pelo operador (Plataforma › Contas › Encerrar, com
  motivo): cancela a assinatura no Asaas, fecha o acesso na hora (sessões
  derrubadas), avisa os administradores por e-mail e marca a data do expurgo
  (90 dias). O login de conta encerrada explica até quando dá para reabrir.
  O operador reabre com "Reabrir" enquanto os dados não foram apagados.
- Job diário `expurgar-contas-encerradas` (05:10): conta sem nenhuma nota é
  excluída inteira; conta com notas fica só com os documentos fiscais (notas,
  eventos, empresas, tomadores das notas sem e-mail/telefone), a cobrança e os
  aceites dos termos — usuários, grupos, contratos, serviços, demais clientes,
  convites, e-mails, histórico, certificados e documentos são apagados.
- Tenant ganha `EncerradaEm`, `EncerramentoOrigem`, `ExpurgoPrevistoEm` e
  `DadosExpurgadosEm` (migration `EncerramentoConta`). Cancelar a assinatura
  continua sem encerrar a conta (Termos 1.1, item 8.5).

### Adicionado (LGPD — exportação dos dados, D1)
- **Tela "Meus dados"** (menu do usuário): o administrador da conta pede a
  exportação de tudo o que a conta tem e baixa um ZIP com o XML oficial e o
  PDF de cada nota autorizada ou cancelada (por empresa, ambiente e mês), os
  cadastros e o histórico em CSV (Excel) e JSON (empresas, clientes, serviços,
  contratos com serviços/histórico de valores/documentos, usuários, notas e
  eventos), os documentos dos contratos e um LEIAME. Certificados e senhas
  ficam de fora.
- Montado em segundo plano (job do Hangfire, um por vez no servidor), com
  aviso por e-mail quando fica pronto; download só logado, por 7 dias; até 3
  pedidos por dia e um em andamento por vez. Limpeza diária
  `limpar-exportacoes-dados` (04:30) apaga o que expirou. Pedido, montagem e
  download ficam no Histórico da conta. Tabela nova `exportacoes_dados`
  (migration `ExportacaoDados`).

### Adicionado (LGPD — retenção dos convites, B7)
- Job diário `limpar-convites-indicacao` (04:20) apaga os convites do
  "Indique e ganhe" e os e-mails de convite com mais de 12 meses, como diz a
  Política de privacidade 1.1 (item 7).

### Corrigido (cobrança)
- **Prêmio de indicação pela conciliação**: quando o webhook do primeiro
  pagamento de uma conta indicada se perde, a conciliação diária com o Asaas
  agora registra os prêmios (os dois lados) e dispara a aplicação, como o
  webhook faria.

### Adicionado (notas)
- **Baixar o XML da nota** autorizada ou cancelada: botão ao lado do PDF na
  lista de notas e "Baixar XML" nos detalhes (`GET /api/nfse/{id}/xml`), com o
  mesmo nome do PDF. Antes o XML só saía anexado no e-mail ao cliente.

### Adicionado (fiscal — Reforma Tributária, F1 parte 2)
- **DANFSe com o IBS/CBS calculado**: o bloco "TRIBUTAÇÃO IBS / CBS" do PDF,
  que saía com traços, mostra o que a SEFIN devolveu na NFS-e — CST e
  classificação, indicador da operação com o município e a UF de incidência,
  base de cálculo, ajustes e reduções, alíquotas (IBS UF/Mun e CBS), alíquotas
  efetivas, valores apurados e, nos totais, o IBS/CBS e o valor líquido + IBS/CBS.
  Valor zero calculado pela SEFIN sai como zero; nota sem o grupo (leiaute
  NT 004) continua como antes.

### Adicionado (fiscal — Reforma Tributária, F1 parte 1)
- **IBS/CBS na DPS** (grupo `IBSCBS`, leiaute NT 009): a nota leva a
  classificação do IBS/CBS — indicador da operação (cIndOp), destinatário =
  tomador (indDest 0), CST e classificação tributária (cClassTrib) e o grupo
  de tributação quando há tributo a declarar. Os valores a SEFIN calcula.
  Validado contra o XSD oficial de Produção Restrita (20/10/2026).
- **Quando vai**: não optante do Simples, em toda nota de competência a partir
  de 01/01/2026 (obrigatório desde 10/2026); optante do Simples (MEI e
  ME/EPP), em produção a partir da competência 01/01/2027 (configurável:
  `NfseNacional__IbsCbsSimplesNacionalAPartirDe`) e, antes disso, já em
  homologação, para testar (`NfseNacional__IbsCbsSimplesNacionalEmHomologacao`).
  Só no leiaute NT 009 (a produção passa a ele em 01/12/2026).
- **Cadastro do Serviço com IBS/CBS**: classificação e indicador da operação,
  com as opções da tabela oficial do item da LC 116 (Anexo VIII v1.02.00) e a
  sugestão que vale quando o campo fica em branco. Indicadores que exigem
  dados que o sistema ainda não envia (imóvel, evento, destinatário diferente
  do tomador) aparecem desabilitados; serviço que só admite esses indicadores
  é barrado na emissão com a explicação, antes de gastar o número da DPS.
- **Tabelas oficiais embutidas** (Anexos VII v1.03.00 e VIII v1.02.00), geradas
  por `tools/ibscbs/gerar_correlacao.py`; `GET /api/catalogos/ibscbs`.
- A nota guarda a classificação enviada (`CIndOp`, `CClassTrib`).
- Orientação de correção para as rejeições do IBS/CBS (E0859, E0860, E0901,
  E0902, E0958–E0961).

### Alterado (jurídico)
- **Termos de uso e Política de privacidade 1.1**, valendo a partir de
  24/11/2026 (aviso prévio de 15 dias, como o item de alterações promete):
  responsável passa a ser a LFMS - Consultoria e Desenvolvimento de Sistemas
  Ltda (CNPJ 04.747.304/0001-78, Lauro de Freitas/BA); teste grátis de 30 dias
  e um por CNPJ; preço de fundador (reserva, perda ao cancelar, troca de plano,
  fora do reajuste); primeira cobrança no fim do teste; atraso não suspende a
  conta, só bloqueia a emissão em produção depois de 10 dias; estorno e
  chargeback; cancelamento pela tela; "Indique e ganhe" e convites por e-mail;
  na Política, Asaas, registro do teste por CNPJ e e-mail dos convidados, com
  prazos de guarda. Os números do texto vêm das constantes do código.
- **Versão 1.0 continua publicada** em /termos/1.0 e /privacidade/1.0.
- **Transição sem bloquear ninguém**: até 23/11 o aceite da 1.0 continua
  liberando o uso; quem ainda não aceitou a 1.1 vê um aviso no topo das telas
  com "Ler e aceitar agora"; a partir de 24/11 a tela de aceite passa a ser
  obrigatória. Cadastro novo já aceita a 1.1.
- **Aviso prévio por e-mail** (job `avisar-novos-termos`, 09:10, só antes da
  data): um e-mail por usuário com e-mail confirmado que ainda não aceitou.
- Razão social, CNPJ e cidade da LFMS no rodapé do site e das páginas legais.

### Testes (segurança)
- **Isolamento entre contas pela API de verdade** (L9): duas contas criadas
  pelo cadastro, já logadas e com CSRF; a conta B, com os ids da conta A, não
  lê, não lista, não altera, não apaga e não usa empresa, cliente, serviço,
  contrato, nota (inclusive PDF e lote), grupo, usuários e auditoria de A
  (sempre 404, com o controle positivo de A vendo o mesmo id), nem entra nas
  telas do operador. O banco é conferido no fim.
- **Trava no modelo**: toda entidade com TenantId precisa ter o filtro de
  conta (ITenantEntity) ou estar na lista de exceções com o motivo
  (Assinatura, EventoCobranca, PremioIndicacao, TesteGratisCnpj, usuário do
  Identity). Entidade nova sem filtro quebra o teste.

### Adicionado (cobrança)
- **Conciliação diária com o Asaas** (job `conciliar-cobranca`, 07:50 de
  Brasília, antes dos avisos): para cada assinatura não cancelada, lê a
  assinatura e todas as faturas no Asaas e corrige o sistema quando um webhook
  se perdeu — pagamento não registrado (Ativa), fatura vencida (Atrasada desde
  o vencimento), estorno/chargeback (Atrasada desde o dia em que soubemos),
  assinatura encerrada no Asaas (Cancelada) e o link da fatura em aberto. O
  pagamento mais recente resolve as pendências anteriores; conta cancelada
  nunca é reativada; assinatura que o Asaas não conhece não muda. Toda
  correção fica no Histórico da conta (`ConciliarCobranca`) e no log.
- **Plataforma › Contas › Cobrança › Conferir com o Asaas agora**, para conta
  que já assinou (a mesma conciliação, sob demanda).
- **Webhook: estorno e chargeback.** PAYMENT_REFUNDED, PAYMENT_REFUND_IN_PROGRESS,
  PAYMENT_CHARGEBACK_REQUESTED, PAYMENT_CHARGEBACK_DISPUTE e
  PAYMENT_RECEIVED_IN_CASH_UNDONE deixam a assinatura Atrasada desde hoje (a
  tolerância de 10 dias conta dali; a disputa não reinicia o prazo);
  PAYMENT_AWAITING_CHARGEBACK_REVERSAL (contestação ganha) e
  PAYMENT_DUNNING_RECEIVED voltam a Ativa.

### Corrigido (busca de notas na SEFIN)
- **"Buscar notas da SEFIN" não dá mais erro interno** quando o ADN recusa a
  consulta: a tela mostra o motivo informado pelo ADN (ou avisa que ele não
  informou) e diz que nada foi alterado; o detalhe cru fica no log como aviso.
  Falha de comunicação vira "tente de novo em alguns minutos".
- **Fila vazia do ADN (404 NENHUM_DOCUMENTO_LOCALIZADO) é "nenhuma nota nova"**,
  não erro.

### Corrigido (emissão e produção)
- **Sem empresa cadastrada, os botões que dependem dela ficam desligados**
  (Emitir nota, Nota Mensal, Buscar notas da SEFIN, Novo cliente, Novo
  serviço, Novo contrato, Verificar reajustes). Antes, clicar mandava a
  requisição sem empresa e aparecia só um erro 404 solto.
- **Colocar em produção exige certificado digital testado na hora**: sem
  certificado, vencido ou sem chave, a passagem é recusada com o motivo; com
  certificado, o sistema faz o teste de conexão com a SEFIN Nacional de
  PRODUÇÃO a cada tentativa e só muda o ambiente se ele passar. O certificado
  testado fica no Histórico (auditoria). Voltar para homologação continua livre.

### Adicionado (site)
- **Página pública /precos** lendo o catálogo (Plataforma › Planos): planos para
  empresas e para contadores/escritórios, preço cheio riscado e preço de
  fundador enquanto houver vaga, vagas restantes e data da campanha, as 3
  regras do teste, comparação com o portal gratuito e dúvidas de cobrança.
  A página inicial usa a mesma tabela (só empresas) e aponta para /precos.
  Menu, rodapé e sitemap com /precos.

### Adicionado (operador)
- **Plataforma › Contas › Cobrança**: prorrogar teste (com motivo), liberar novo
  teste (inclusive para CNPJ que já tinha usado) e isentar de cobrança até uma
  data ou sem prazo, pela tela. Só para conta sem assinatura no Asaas; tudo no
  Histórico da conta.
- **Números de venda** no topo da tela (em teste, assinantes, MRR, conversão,
  vagas de fundador) e **filtro por situação** na lista de contas.

### Adicionado (teste grátis)
- **Um teste grátis por CNPJ**: a conta de empresa cujo CNPJ já usou o teste
  em outra conta (mesmo excluída) não ganha teste; emite em produção só com
  assinatura, e a tela Assinatura explica o motivo. Contas de contador não
  entram na regra.

### Adicionado (campanha de lançamento)
- **Vaga de fundador guardada para quem entra**: ao confirmar o e-mail durante a
  campanha, a conta reserva o preço de fundador do grupo (empresas ou
  escritórios). Vale até 7 dias depois do fim do teste; no beta, sem prazo.
  Também reserva ao abrir Assinatura e na varredura diária.
- **Data-fim da campanha** por grupo em Plataforma › Planos, com reservas e
  situação (aberta/encerrada) no card das vagas.
- **Primeira cobrança no fim do teste**: quem assina durante o teste não perde
  nenhum dia; a primeira fatura vence quando o teste acaba (assinar antes de
  emitir já começa o teste).
- **Cancelar mantém os dias pagos**: a emissão continua até o fim do teste ou do
  mês já pago; o cancelamento avisa a perda do preço de fundador.
- Tela Assinatura: linhas "Preço de fundador" e "Primeira cobrança".

### Alterado (teste grátis)
- **Teste grátis só por dias** (30 dias, sem limite de notas). Sai o aviso de
  "8 de 10 notas".

### Alterado (cobrança)
- **Tolerância de atraso de 10 dias** (antes 7): a emissão em produção continua
  até 10 dias depois do vencimento e bloqueia no 11º, como dizem os Termos de
  uso. Avisos, faixa do painel e e-mails acompanham a nova data.

### Corrigido (deploy)
- **Rollback do Deploy Production**: a imagem em produção ganha o nome
  `nfsesaas-app:anterior` antes do build. Com o Docker 29 o rollback antigo
  falhava ("No such image") porque a imagem sem nome some depois do build.
  O rollback não para no primeiro erro e não recompila a imagem.

### Alterado (.NET 10)
- **Plataforma migrada para o .NET 10 (LTS)**: o suporte do .NET 8 termina em
  10/11/2026. EF Core, Identity e Npgsql 10, Serilog 10, Swashbuckle 10.
  Certificados carregados pelo `X509CertificateLoader` (substitui o
  construtor obsoleto do `X509Certificate2`). CI com SDK 10 e `dotnet-ef` 10.
  Sem mudança de banco nem de tela.

### Alterado (catálogo de planos)
- Os planos iniciais são criados **só com o catálogo vazio** (banco novo).
  Depois disso o catálogo é todo do operador: qualquer plano nunca assinado
  pode ser excluído, inclusive Empresa e Escritórios, e não volta quando o
  sistema liga. Plano já assinado continua só com "Fora de venda".

### Adicionado (excluir plano)
- **Plataforma › Planos › Excluir**: plano que nunca foi assinado (nem
  cancelado) pode ser excluído, com código de 6 dígitos no
  e-mail do operador (10 min, uso único, 5 tentativas). Apaga o plano e o
  histórico de preços; registro `ExcluirPlano` na Auditoria. Plano já
  assinado: usar "Fora de venda".

### Adicionado (catálogo de planos e plano Escritório)
- **Plataforma › Planos** (só operador): planos, preços e vagas de fundador
  saem do código e vão para o banco (`planos`, `precos_planos`,
  `vagas_fundador`). Criar e alterar plano (nome, descrição, para quem aparece,
  empresas e notas incluídas, à venda, ordem).
- **Preço com vigência e histórico**: novo preço a partir de hoje ou de uma
  data futura (agendado); mesma data corrige. Quem já assina mantém o preço
  contratado; o novo vale só para novas assinaturas.
- **Planos Escritório 10/25/50** para contadores (R$ 129/279/499; fundador
  R$ 89/199/349), notas sem limite. Contador vê Empresa + Escritórios; empresa
  vê Mico + Empresa. Assinar recusa plano fora de venda ou de outro perfil.
- **Vagas de fundador separadas** (Empresas 20, Escritórios 10), editáveis;
  a tela Assinatura mostra quantas vagas restam em cada plano.
- Toda alteração fica no Histórico do operador.
- Migration `CatalogoPlanos` (3 tabelas novas; os dados iniciais entram pelo
  `PlanosSeeder` no startup). `assinaturas.Plano` continua com os mesmos
  códigos (1 = Mico, 2 = Empresa).

### Adicionado (cadastro em etapas)
- **Cadastro em 3 etapas**: "Você é" (minha empresa / contador) → documento →
  e-mail e senha (com confirmação da senha). Perfil empresa: ao digitar o
  CNPJ, a tela busca na Receita e mostra "Sua empresa: XYZ LTDA, Salvador/BA";
  CNPJ baixado/inapto não segue; consulta fora do ar ou lenta deixa digitar
  o nome. Consulta pública com limite por IP (`ConsultaCadastro`).
- **Contador pode abrir conta com CPF** (autônomo) ou CNPJ (escritório).
  A conta passa a ter **documento (CPF ou CNPJ)** e **perfil** (Empresa ou
  Contador); o operador vê o selo "Contador" em Plataforma › Contas.
- **Empresa criada junto** no perfil empresa, com os dados públicos do CNPJ
  (endereço, município, telefone, sugestão de Simples/MEI) e o e-mail da
  conta. O que não vem da Receita (Inscrição Municipal, regime) fica como
  pendência: os Primeiros passos mostram "Complete os dados da empresa" e a
  emissão diz exatamente o que falta.
- **Confirmação do e-mail**: link enviado no cadastro; aviso no topo com
  "Reenviar e-mail" e passo nos Primeiros passos. Sem confirmar, usa tudo e
  emite em homologação, mas **não emite em produção** (tela, lote e
  automação). Convite aceito e senha redefinida pelo link também confirmam.
- **Certificado A1 conferido**: o CNPJ do certificado (SAN ICP-Brasil ou nome)
  precisa ter a mesma raiz do CNPJ da Empresa (matriz/filial aceitas).
- Testes de integração usam uma consulta de CNPJ falsa (sem BrasilAPI).
- Migration `CadastroEmEtapas`: `tenants.Cnpj` renomeada para `Documento`,
  nova coluna `Perfil` (1 = Empresa nas contas existentes) e usuários
  existentes marcados com e-mail confirmado.

### Adicionado (exclusão de conta)
- Plataforma › Contas: **excluir conta definitivamente** (lixeira), com
  confirmação por **código de 6 números enviado ao e-mail do operador**
  (10 minutos, uso único, 5 tentativas). O código sai direto pelo SMTP e não é
  gravado (nem no EmailLog); o servidor guarda só o hash, em memória.
- Só exclui conta que **nunca registrou nenhuma nota** (qualquer status ou
  ambiente), **sem assinatura ativa** no Asaas e que não seja a do operador.
- Apaga em cascata, numa transação: empresas, clientes, serviços, contratos
  (histórico e documentos), Nota Mensal, usuários, grupos, histórico, e-mails,
  aceites, assinatura cancelada, eventos de cobrança e prêmios; depois os
  certificados e documentos em disco e os jobs agendados. Fica só um registro
  no Histórico do operador, sem nome nem CNPJ. Guia em `docs/operacao/contas.md`.

### Corrigido
- Conta nova (sem Empresa) aberta num navegador que já tinha usado outra
  conta: o cookie "Empresa atual" da conta anterior fazia todas as telas
  pedirem dados de uma Empresa de outra conta, e o servidor recusava tudo
  ("Empresa não encontrada"). Agora o cookie é apagado quando a conta não
  tem Empresa (e ao sair), e as telas mostram "Cadastre a sua empresa para
  começar" com o atalho para a tela Empresas.

### Adicionado (prêmio do indique e ganhe)
- **1 mês grátis automático** para os dois lados quando uma conta indicada
  paga a primeira fatura: a próxima fatura em aberto é removida no Asaas (ou o
  próximo vencimento é adiado, se ela ainda não foi gerada). Quem indicou e
  ainda não paga fica com o prêmio guardado até a assinatura estar em dia.
- Concedido uma única vez por indicação e por lado (índice único); gravado
  como "Aplicando" antes de chamar o Asaas, para nunca dispensar duas faturas
  se o sistema cair no meio.
- Job `aplicar-premios-indicacao` (logo após o pagamento e todo dia às 07:40).
- Plataforma › Contas: quadro **Prêmios de indicação** com situação, detalhe
  e "Tentar de novo" para os que deram erro.
- Indique e ganhe: cada conta indicada mostra "1 mês grátis ganho" ou
  "guardado"; texto do prêmio atualizado.
- Webhook: `PAYMENT_DELETED` limpa o link da fatura removida;
  `PAYMENT_RESTORED` volta a registrar a fatura em aberto.
- Migration `PremioIndicacao`: tabela `premios_indicacao`.
- Testes de integração sobem com uma factory própria (`AplicacaoTesteFactory`)
  que zera `Asaas:ApiKey` e `Asaas:WebhookToken`: com a chave do sandbox nos
  user-secrets, um job real disparado pelo teste chamava o Asaas.

### Adicionado
- `Cobranca:ContasPiloto`: contas em que a cobrança funciona mesmo com
  `Cobranca:Ativa=false`, para testar o ciclo completo em produção (assinar,
  pagar no sandbox, webhook ativar) com uma conta de teste, sem ligar a
  cobrança para os clientes do beta.

### Adicionado (cobrança)
- **Planos** Mico (R$ 29) e Empresa (R$ 59), com preço de fundador (R$ 19 e
  R$ 39) para os 20 primeiros assinantes, mantido enquanto a assinatura
  existir. Limites de empresas e notas por mês são informativos.
- **Teste grátis** em produção: 30 dias ou 10 notas, contados da primeira nota
  em produção; homologação sempre livre.
- **Bloqueio só da emissão em produção** (tela, lote e automação) quando o
  teste acaba sem assinatura, a fatura está vencida há mais de 7 dias ou a
  assinatura foi cancelada. Consulta e download nunca são bloqueados.
- Tela **Assinatura** (Administrador; Financeiro só consulta): situação, uso
  do mês, planos, assinar, pagar fatura e cancelar. Nova tela na matriz de
  permissões.
- Integração com o **Asaas**: cliente e assinatura mensal (Pix, boleto ou
  cartão à escolha do cliente), link da fatura, cancelamento. Webhook
  `POST /api/webhooks/asaas` autenticado por token, idempotente pelo id do
  evento.
- Painel do operador (Contas) mostra a situação da cobrança de cada conta.
- **Desligada por padrão** (`Cobranca:Ativa=false`): o beta continua grátis
  até a cobrança ser ligada. `Cobranca:ContasIsentas` para a conta do operador.
  Configuração em `docs/operacao/cobranca.md`.
- Migration `CobrancaAssinatura`: tabelas `assinaturas` (uma por conta) e
  `eventos_cobranca`.

### Adicionado (indique e ganhe)
- Tela **Indique e ganhe** (menu lateral, qualquer usuário logado): link da
  conta (`micoempresa.com.br/?ref=CODIGO`), botões de copiar, WhatsApp e
  e-mail, e a lista das contas que vieram pelo link (só data e situação, sem
  nome). O prêmio (1 mês grátis para os dois quando a indicada virar
  assinatura) entra com a cobrança.
- O código de indicação (`?ref=`) e o canal (`?origem=`, ex.: `email-nota`)
  viajam no endereço da página pública até o formulário de cadastro, sem
  cookie. Código desconhecido é ignorado e nunca impede o cadastro.
- Conta nova já nasce com o próprio código; contas antigas ganham o código na
  primeira visita à tela.
- Painel do operador (Contas) mostra quem indicou cada conta, a origem do
  cadastro e quantas indicações cada uma fez.
- Texto da tela de cadastro sem o termo "Tenant".
- Migration `IndicacaoDeContas`: 3 colunas opcionais em `tenants`
  (`CodigoIndicacao` único, `IndicadoPorTenantId` com FK para `tenants`,
  `OrigemCadastro`).

### Corrigido (número da NFS-e)
- O "Número da NFS-e" guardava o Id da DPS ("DPS" + 42 dígitos) em vez do
  número oficial gerado pela SEFIN. Agora a emissão grava o `nNFSe` do XML
  autorizado, e as notas antigas são corrigidas sozinhas na subida do
  sistema (`NumeroNfseCorrecao`, idempotente, sem migration): com XML, recebem
  o número oficial; sem XML (rejeitadas), ficam sem número de NFS-e.
- Lista de notas e painel mostram o número da NFS-e em destaque e a DPS
  embaixo. O e-mail ao tomador já usava esse campo e passa a mostrar o
  número certo.

### Adicionado (onboarding)
- **Primeiros passos** no painel: da conta nova até a primeira nota em
  produção, em 6 passos (empresa, certificado A1, serviço, cliente, nota de
  teste, produção), com barra de progresso, o próximo passo em destaque e
  botão para a tela certa. Cada passo é deduzido do que já existe na conta
  (sem migration) e o cartão some quando tudo está feito. "Ocultar por
  agora" vale no navegador até o próximo passo ser concluído.
  Endpoint `GET /api/onboarding`.
- Página pública **Certificado digital A1** (`/ajuda/certificado-digital-a1`):
  qual certificado serve (A1, não A3), onde comprar, como exportar o .pfx no
  Windows, como enviar e os problemas comuns. Linkada no "Primeiros
  passos", no modal do certificado em Empresas, no menu lateral e no rodapé
  do site; entra no sitemap.

### Corrigido
- `robots.txt` bloqueava o próprio `/sitemap.xml` (o Search Console acusava
  "Bloqueada pelo robots.txt" e "Erro de HTTP geral"). Agora libera o site e
  bloqueia só as áreas privadas (`/Account/` exceto o cadastro, `/api/`,
  `/hangfire`, `/Legal/`).

### Adicionado (site público)
- Página inicial pública em "/" para quem não está logado: apresentação,
  recursos, como funciona, preços (Mico e Empresa, com preço de fundador e
  beta gratuito), dúvidas frequentes e cadastro. Quem está logado continua
  vendo o painel.
- **Erros da NFS-e explicados**: `/erros-nfse` (lista com busca) e
  `/erros-nfse/{codigo}` (uma página por código, com a mensagem oficial, o
  campo, o anexo de origem, como resolver e erros relacionados). São os 579
  códigos do catálogo oficial, com título, descrição e endereço canônico para
  o Google. Código em minúsculas redireciona para o endereço oficial.
- `/sitemap.xml` e `/robots.txt` (só as páginas públicas são indexáveis).
- E-mail da nota ao tomador ganha uma linha de divulgação do micoempresa no
  rodapé.

### Testes
- Testes de integração das páginas públicas. Os testes que sobem a
  aplicação em memória agora compartilham um único host (duas instâncias em
  paralelo disputavam o logger de bootstrap do Serilog e falhavam às vezes).

### Segurança (chaves do Data Protection)
- Plataforma › Contas, cartão **Proteção das chaves**: mostra se as chaves que
  abrem os certificados A1 estão criptografadas e oferece **Proteger chaves
  antigas**. A ação cria uma chave nova criptografada, regrava todos os
  certificados com ela, confere e só então apaga as chaves antigas em texto
  aberto. Se algo falhar, nada é apagado. A subida do sistema avisa no log
  enquanto houver chave antiga sem criptografia.
- `deploy/protecao-chaves/configurar.sh`: gera na VPS o certificado de
  proteção (`/opt/nfsesaas/segredos`, fora do git e do backup) e o
  `docker-compose.segredos.yml`. A opção `--restaurar` recria os dois a
  partir do gerenciador de senhas. Procedimento em
  `docs/operacao/protecao-chaves.md`.

### Operação
- Deploy Production: usa o `docker-compose.segredos.yml` quando ele existe
  (também no rollback) e passa a guardar só os 10 backups pré-deploy mais
  recentes em `/opt/nfsesaas/backups/postgres`.

### Segurança (dependências)
- `System.Security.Cryptography.Xml` 8.0.4 → 8.0.5: corrige GHSA-7hqp-7c5m-4vq6,
  GHSA-c399-9853-6jmj, GHSA-r955-8q5j-3h83 e GHSA-vrv5-2jg9-4mmr (aviso
  NU1903) e traz o `System.Security.Cryptography.Pkcs` 8.0.2, que corrige
  GHSA-846f-f764-76mx e GHSA-2fmv-rv2c-59x7.
- `Newtonsoft.Json` fixado em 13.0.4: o Hangfire puxava a 11.0.1
  (GHSA-5crp-9r3c-p9vr). Com isso, `dotnet list package --vulnerable
  --include-transitive` não aponta mais nada na solução.

### Corrigido (build e EF)
- Build sem nenhum aviso. O `AddOrUpdate` obsoleto do Hangfire passou para a
  forma com `RecurringJobOptions`; foram tratados os nulos em
  `CertificateHttpMessageHandlerBuilderFilter` (HttpClient sem nome) e em
  `DpsSigner` (`SignedInfo`).
- Avisos de "sentinel" do EF na subida: o valor padrão do banco agora está
  declarado em todos os campos. `Contrato.Status` usa um sentinel fora do
  enum, então um contrato gravado como **Rascunho** (0) não vira mais
  **Ativo** pelo default do banco no INSERT. Sem migration: o esquema do
  banco não muda.

### Alterado (tela de notas)
- Emissão em duas etapas: "Revisar" mostra o resumo (cliente, contrato,
  serviço, competência, valor, ISS, ambiente e a descrição que vai na nota)
  e só "Confirmar e emitir" manda para a SEFIN. Vale também para o reenvio.
- Detalhe da nota reorganizado em blocos: Nota, Cliente e serviço (com código
  de tributação e NBS), **Descrição na nota** (o texto que foi para a SEFIN,
  antes não aparecia), Valores e tributação, e Erro. O JSON do snapshot virou
  "Dados técnicos", recolhido e com explicação: `descricaoCadastro` é o nome
  do serviço no catálogo, não o texto da nota.
- Campo de descrição com contador de caracteres e ajuda ("é este texto que
  sai na nota").
- `NfseResponse` passa a trazer `codigoTributacaoNacional`, `codigoNbs`,
  `tpRetIssqn`/`issRetido` e `aliquotaIss`.

### Corrigido (tela de notas)
- Trocar serviço, contrato ou cliente sobrescrevia em silêncio a descrição e
  o valor já digitados — inclusive quando a busca de contratos do cliente
  respondia depois da digitação. Agora o que foi digitado é mantido, com o
  link "Usar os do cadastro" para voltar ao sugerido.
- Chave de idempotência da emissão passa a ser uma por abertura do modal (era
  uma por clique, então confirmar de novo depois de um erro de rede podia
  duplicar a nota).
- Descrição de serviço com aspas ou "<" quebrava a lista de serviços do modal
  (texto não era escapado).

### Adicionado (rejeições da SEFIN)
- Catálogo com os 579 códigos de rejeição oficiais da NFS-e Nacional,
  extraídos das planilhas do gov.br: Anexo I DPS/NFS-e v1.01 (produção
  20260209 e produção restrita 20261020), Anexo II Eventos v1.01 (20260122 e
  20261020) e Anexo VI IBS/CBS v1.02.01.
- Nota rejeitada agora mostra, além da mensagem oficial, **onde corrigir**
  (empresa, configuração fiscal, certificado, cliente, serviço, dados da
  nota, cancelamento, IBS/CBS ou suporte), **o que fazer** e um atalho para a
  tela. Vale para o detalhe da nota, o aviso de reenvio e os avisos de
  emissão e de cancelamento recusados. Há texto específico para as rejeições
  mais comuns (certificado, CNPJ e inscrição municipal, Simples Nacional,
  município sem convênio, código de tributação, CEP do cliente, alíquota e
  retenção de ISS, competência, DPS duplicada).
- A API devolve isso em `rejeicao` (`NfseResponse`, `EmitirNfseResult`,
  `CancelarNfseResult`). Código fora do catálogo (regra nova) continua com a
  mensagem recebida.
- `tools/rejeicoes-sefin/gerar_catalogo.py` regenera o catálogo quando a
  SEFIN publicar planilhas novas.

### Corrigido (tela de notas)
- A mensagem de erro da SEFIN no detalhe da nota passa a ser escapada antes
  de entrar no HTML.
- Aviso de erro longo fica mais tempo na tela (até 15 segundos).

### Adicionado (monitoramento)
- Alerta por e-mail ao operador da plataforma para todo erro registrado no
  log (exceção não tratada, falha de job em uma Empresa) e para job que
  falhou de vez no Hangfire, mesmo depois das novas tentativas. Repetições do
  mesmo erro são agrupadas (1 e-mail a cada 30 minutos, com a contagem) e há
  um teto de 20 e-mails por hora. Os alertas ficam na tela de E-mails da
  conta do operador ("Alerta do sistema").
- E-mail aos administradores da conta quando o job diário gera reajuste de
  contrato **aguardando aprovação** ou **aplicado automaticamente**, um por
  Empresa por dia ("Reajustes de contrato").
- `docs/operacao/monitoramento.md`, com a configuração do UptimeRobot no
  `/health` e as regras dos alertas.

### Alterado (monitoramento)
- Respostas esperadas (regra de negócio, validação, não encontrado, sessão
  expirada) passam a ser registradas como Warning, e não mais como Error:
  não são falha do sistema e não devem virar alerta.

### Adicionado (e-mails)
- Nota autorizada vai sozinha para o e-mail do tomador, com o PDF (DANFSe)
  e o XML anexos — vale para emissão pela tela, nota mensal em lote e
  automação. Nova opção no cadastro do cliente, "Enviar a nota por e-mail"
  (ligada por padrão, inclusive nos clientes que já existem). Botão
  "envelope" na lista de notas para mandar (de novo) a qualquer momento.
  Os anexos são gerados na hora do envio, a partir da própria nota (o
  "Reenviar" da tela de E-mails também reanexa); sem PDF disponível, vai só
  o XML. Nota de homologação sai com "[TESTE]" no assunto.
- Aviso de certificado digital vencendo: job diário (08:20 de Brasília)
  manda e-mail aos administradores da conta e ao e-mail da empresa quando
  faltam 30, 15 e 7 dias e quando vence — cada aviso uma vez só por
  certificado; renovou, os avisos param.
- Tela de E-mails: tipos "Nota ao cliente" e "Certificado vencendo".
- Migration `NotaPorEmailEAvisoCertificado` (`clientes.NaoEnviarNfsePorEmail`,
  `email_logs.NfseId` e `email_logs.ChaveDeduplicacao`).

### Corrigido (e-mails)
- E-mail de destinatário inválido virava exceção solta no envio e o
  registro ficava "Pendente" para sempre; agora fica "Falhou" com o motivo.

### Adicionado (LGPD)
- Termos de uso e Política de privacidade (modelo para revisão jurídica),
  públicos em `/termos` e `/privacidade`, com links no login, no cadastro,
  no convite e no menu do usuário. Versões e dados do responsável em
  `NfseSaaS.Domain.Legal.DocumentosLegais` (único lugar a mudar).
- Aceite registrado: o cadastro e o aceite de convite exigem marcar "Li e
  aceito"; cada aceite grava versão dos dois documentos, data, IP,
  navegador, usuário e origem (tabela `aceites_termos`, nunca alterada) e
  entra no histórico de auditoria (`AceitarTermos`). Migration `AceiteTermos`.
- Usuário logado sem aceite da versão vigente (contas já existentes, ou
  quando a versão sobe) vai para a tela de aceite antes de qualquer outra;
  a API responde 403 `termos_pendentes` e as telas levam ao aceite sozinhas.
  Depois de aceitar, volta para onde estava.

### Adicionado (operação)
- Backup diário externo da produção para o OneDrive, criptografado (rclone
  crypt): banco (`pg_dump` validado), certificados com as chaves do Data
  Protection, documentos dos contratos e configuração do servidor. Retenção
  de 7 diários, 4 semanais e 3 locais, todo dia às 03:30 de Brasília.
  Script, cron e logrotate em `deploy/backup/`. Instalação, ensaio mensal de
  restauração e roteiro de recuperação em
  `docs/operacao/backup-restauracao.md`.
- `.gitattributes`: arquivos `*.sh` sempre com LF.

### Adicionado (plataforma)
- Painel do operador da plataforma (menu Plataforma › Contas, só para quem
  está em `Plataforma:UsuariosOperadoresIds`): todas as contas com
  responsável, usuários, empresas (e quantas em produção), notas autorizadas
  no mês e no total, última nota e situação; atalho para o Hangfire.
- Suspender/reativar conta (com motivo): ninguém da conta entra (login
  responde "conta suspensa"), as sessões abertas caem em até 1 minuto, a
  emissão é recusada (tela, lote e automação) e os jobs pulam a conta. Fica
  registrado no histórico (AuditLog) da conta e no log. O operador não pode
  suspender a própria conta. Migration `SuspensaoDeConta`.

### Alterado (visual)
- Novo visual e nova marca: **micoempresa** (NFS-e passa a ser um módulo).
  Tema refeito: cores com contraste AA, campos de 40px com asterisco
  automático nos obrigatórios, status em pílulas suaves com ponto, tabelas
  sem listras e com as ações lado a lado (antes empilhavam e cada linha
  ficava enorme), abas sublinhadas nos modais, select2 alinhado aos demais
  campos, paginação/busca do DataTables no mesmo estilo, tema escuro revisado.
- Menu lateral reorganizado: Notas fiscais (NFS-e) · Cadastros e referência
  · Administração. Chip de ambiente: Homologação em âmbar, Produção em verde.
- Tela de login dividida: marca e benefícios à esquerda, formulário à direita.
- Botão do menu sempre visível: no computador recolhe o menu lateral para só
  ícones (com dica ao passar o mouse; a escolha fica salva no navegador); no
  celular abre o menu por cima da tela. Tabelas largas rolam dentro do card,
  abas dos modais rolam de lado no celular.
- Modal de Empresa: seções num menu lateral com ✓ (completa) e ! (falta campo
  obrigatório), cabeçalho com CNPJ, situação e ambiente; código IBGE do
  município passou para a seção Endereço.
- Modal de Cliente: uma tela só, em seções (Identificação, Contato, Endereço,
  Fiscal), com "Retém ISS" em cartão com interruptor.
- Modal de Contrato: cabeçalho com cliente e contrato, abas com ícones e um
  resumo fixo à direita (valor, situação, próximo reajuste, índice, regra do
  aniversário e o reajuste pendente com atalho "Decidir agora").
- Empresa e Cliente: digitar o CEP preenche rua, bairro, UF e o código IBGE
  do município (ViaCEP), e o nome do município aparece ao lado do código
  (API do IBGE, com cache). Consultas feitas pelo servidor e fora do circuit
  breaker da SEFIN.

### Adicionado
- Reajuste automático no aniversário do contrato: todo dia (10:10) o sistema
  calcula o acumulado de 12 meses do índice do contrato e cria o reajuste no
  Histórico de valores. Empresa define o padrão (aplicar automaticamente ou
  aguardar aprovação) e cada contrato pode escolher diferente. Aguardando
  aprovação: aplicar o índice, negociar outro % ou valor (com motivo) ou não
  reajustar neste ciclo (com motivo). Índice acumulado negativo mantém o valor,
  a menos que o contrato marque "índice negativo reduz o valor". Botão
  "Verificar reajustes" na tela de Contratos roda o cálculo na hora; selo
  "Reajuste a aprovar" na lista e contagem no Dashboard. Aniversários com mais
  de 60 dias de atraso ficam para o reajuste manual.
- Nota Mensal avisa quando o contrato tem reajuste aguardando aprovação na
  competência, ou quando o valor já inclui um reajuste com vigência posterior.
- Quadros de resultado ("Importar agora", "Verificar reajustes") com botão de
  fechar; sucesso fecha sozinho em 10 s com contador (pausa com o mouse em cima).

### Corrigido (reajuste)
- Com um reajuste aguardando aprovação, registrar outro reajuste (manual ou
  pelas linhas de serviço) é bloqueado — eram dois valores novos concorrentes.
- Aviso de reajuste na emissão avulsa mostrava o índice como número (e o IPCA
  sumia da mensagem).
- Tela "Índices econômicos" (menu lateral): variação mensal de IPCA, IGP-M,
  INCC, INPC, Selic e CDI, importada todo dia (09:40) da API SGS do Banco
  Central — 24 meses na primeira vez. Mostra o acumulado dos 12 últimos meses
  já publicados para qualquer data de aniversário, com os meses usados; recusa
  calcular quando falta mês ou o índice está desatualizado. O operador da
  plataforma pode lançar/corrigir um mês à mão (fica "Manual" e a importação
  não sobrescreve), excluir e "Importar agora". Base do reajuste automático.
- Histórico de valores do contrato: substitui o "Histórico de reajustes".
  Cada mudança de valor é um evento com tipo (Valor inicial, Reajuste,
  Alteração de escopo), data de vigência, valor anterior → novo, %, índice,
  motivo e os serviços cobrados a partir dele. Contrato novo já nasce com o
  evento de Valor inicial. A migration `HistoricoValoresContrato` converte
  os reajustes existentes (sem perda) e cria o Valor inicial de cada contrato.
- Editar os serviços de um contrato pede o tipo da mudança (reajuste ou
  alteração de escopo), a vigência e o motivo, e registra no histórico.

### Corrigido
- Telas de Empresas, Clientes, Serviços, Contratos e Notas Fiscais decidiam
  os botões de incluir/editar/emitir/cancelar pelo papel antigo do Identity
  ("Administrador"/"Emissor"), que contas criadas pelo cadastro e grupos
  personalizados não têm — esses usuários não viam os botões, embora a API
  permitisse. Agora usam a permissão do Grupo, a mesma que a API confere.
- Reajuste com data futura deixa de ser aceito (o valor novo passava a valer
  na hora, antes da data informada).

### Alterado
- Contratos: ao editar, a aba "Serviço" ficava vazia (o bloco era escondido).
  Agora mostra as linhas e permite incluir, remover e alterar quantidade e
  valor dos serviços de um Contrato existente. O valor do Contrato é
  recalculado e a alteração aparece no Histórico ("Serviços alterados:
  R$ X → R$ Y"). Notas já emitidas não mudam; reajuste por índice continua
  em "Registrar reajuste". Linha com Serviço desativado continua aparecendo
  (marcada "inativo").
- Retenção de ISS passou da Empresa para o Cliente ("Este cliente retém o
  ISS na fonte") e pode ser ajustada em cada nota. Com ISS retido, a DPS
  passa a enviar a alíquota (`pAliq`), da nota ou da nova "Alíquota de ISS
  (%)" da Empresa — antes uma Empresa marcada como "retido" emitia TODAS as
  notas retidas e sem alíquota. Nota Mensal e automação usam o padrão do
  Cliente. A migration `RetencaoIssPorCliente` marca como "retém ISS" os
  Clientes de Empresas que estavam com retenção.
- Tributação ISSQN restrita a "Operação tributável": imunidade, exportação
  e não incidência exigem grupos que a DPS ainda não monta (toda nota dessas
  Empresas seria rejeitada). Retenção pelo intermediário também bloqueada.

### Segurança
- Painel `/hangfire`, tela Logs e tela Sobre agora são exclusivos do
  operador da plataforma (`Plataforma:UsuariosOperadoresIds`). Antes
  qualquer Administrador de tenant — inclusive quem acabou de se cadastrar
  — via os logs de todos os clientes e podia disparar jobs (inclusive
  emissão) de outros tenants. As telas Logs e Sobre saem da matriz de
  permissões dos grupos (removidas na subida, em bancos existentes).
- Bloquear um usuário derruba a sessão aberta dele em até 1 minuto
  (antes valia até o cookie expirar), e "esqueci a senha" não desfaz mais
  um bloqueio feito pelo Administrador. Mudanças de grupo/permissão passam
  a valer em até 1 minuto, sem relogar.
- Emissão e cadastro de Contrato conferem se Cliente e Serviço pertencem
  à mesma Empresa.
- Chaves do Data Protection (que abrem os certificados dos clientes)
  podem ser criptografadas com um certificado próprio
  (`DataProtection:CertificadoPfxPath`/`CertificadoSenha`) e ter pasta
  própria (`DataProtection:KeysPath`). Compatível com as chaves atuais; a
  subida registra no log onde estão as chaves e se estão protegidas.

### Corrigido
- Envio de DPS e de evento de cancelamento não é mais reenviado
  automaticamente em falha de rede (retry só em consultas GET). Reenviar
  às cegas depois de um timeout podia gerar NFS-e em duplicidade.
- Sincronização ADN: nota que ficou em "falha de comunicação" mas foi
  autorizada pela SEFIN agora é confirmada com os dados oficiais (antes a
  nota autorizada era descartada como conflito de numeração). Notas em
  que a Empresa não é a emitente (ex.: recebidas de fornecedor) deixam de
  ser importadas.
- Cadastro de nova conta: Tenant e usuário são criados numa transação só
  (antes uma senha recusada deixava o CNPJ travado com erro 500), com CNPJ
  validado e mensagem clara para CNPJ já cadastrado. Novo
  `Plataforma:CadastroAberto` para fechar o auto-cadastro.
- Telefone da Empresa com máscara quebrava toda DPS (E1235); agora vai só
  com dígitos e o cadastro valida 6 a 20 dígitos.
- Evento de cancelamento usava o horário do servidor (UTC no container),
  mesmo problema que causava E0008 na emissão.
- Emissão em Homologação rejeitada com E1235 ("invalid child element
  'cLocEmi' ... expected: 'cMotivoEmisTI, finNFSe'") desde que a
  Produção Restrita passou a exigir o leiaute da NT 009 (esquemas de
  20/10/2026). A DPS agora sai com `finNFSe=0` (NFS-e regular) entre
  `tpEmit` e `cLocEmi`, e com `xDescServ` como último elemento de `cServ`.
- Tomador pessoa física (CPF) gerava `<CNPJ>` com 11 dígitos e a DPS era
  rejeitada (E1235). Agora sai `<CPF>`; CPF/CNPJ com máscara são limpos
  antes de ir pro XML (letras do CNPJ alfanumérico são preservadas).
- Empresa Não optante pelo Simples não conseguia emitir: o grupo
  obrigatório `totTrib` era omitido. Agora sai `indTotTrib=0`; optantes
  (MEI e ME/EPP) continuam com `pTotTribSN`.
- `regApTribSN` só é enviado para optante ME/EPP (antes ia para qualquer
  regime, inclusive vazio), e passou a ser obrigatório nesse caso.
- Inscrição municipal, telefone e e-mail do prestador vazios não geram
  mais elemento vazio no XML.
- Cadastro de Empresa: a lista "Regime especial de tributação" tinha os
  textos trocados em relação aos códigos enviados à SEFIN (ex.: "Sociedade
  de Profissionais" mandava o código 2, que é "Estimativa"). Textos agora
  seguem o XSD oficial (`TSRegEspTrib`), também no DANFSe, e o cadastro só
  aceita os códigos 0–6 e 9.
- Cadastro de Empresa: "CST PIS/COFINS" só oferecia 00–04; agora traz
  todos os CSTs de saída da tabela oficial (00–09, 49 e 99). Tributação
  e retenção de ISSQN e PIS/COFINS passaram a ser validadas contra os
  códigos do XSD.
- Emitir nota: ao escolher o Cliente, os Contratos dele não carregavam
  (o campo "Contrato" ficava vazio). O select2 dispara `change` via jQuery,
  que não chega a listener nativo (`addEventListener`). Agora usa `.on()`.
- Emitir nota: escolher um Contrato deixava o Serviço em branco (lia
  `contrato.servicoId`, que não existe desde que o Contrato passou a ter
  várias linhas de serviço). Agora seleciona a primeira linha, com o valor
  da linha; com mais de um serviço, avisa que a nota avulsa sai para o
  serviço escolhido e indica a Nota Mensal para faturar o contrato inteiro.
- DANFSe: "Retenção PIS/COFINS" só reconhecia os códigos 0 e 1 (com texto
  errado no 1); demais saíam "-". Agora cobre 0–9 conforme o XSD.
- DANFSe: campo "Finalidade" lê `finNFSe` da nova posição (raiz do
  `infDPS`, NT 009), com fallback para o local antigo em notas anteriores.

### Adicionado
- Leiaute da DPS configurável por ambiente
  (`NfseNacional:LeiauteDpsHomologacao` / `NfseNacional:LeiauteDpsProducao`,
  valores `Nt004` ou `Nt009`). Padrão: Homologação `Nt009`, Produção
  `Nt004`. Na entrada da NT 009 em Produção (previsão 01/12/2026), trocar
  só `LeiauteDpsProducao` para `Nt009`, sem deploy.
- Teste que valida a DPS gerada contra os XSDs oficiais de Produção
  Restrita (`tests/NfseSaaS.Tests/Schemas/ProducaoRestrita_20261020`).

## [1.2.0] - 2026-09-16
### Adicionado
- Snapshot fiscal da nota, na tela de detalhe, agora é colapsável
  (some por padrão, evita modal gigante).
- Toasts passaram a empilhar múltiplas mensagens (antes uma
  sobrescrevia a outra), com ícone de sucesso/erro, botão de fechar e
  barra de progresso.

### Alterado
- Todo modal passou a abrir centralizado verticalmente na tela (antes
  abria colado no topo), com corpo de altura limitada e scroll
  interno, cantos mais suaves e fundo com leve desfoque.

### Corrigido
- Botões de ação em tabela (editar, excluir, ativar/desativar etc., em
  Empresas, Clientes, Serviços, Contratos, Usuários, Grupos, E-mails e
  Nfse) não desabilitavam durante a requisição — clique repetido antes
  da tela atualizar disparava a mesma ação várias vezes seguidas.

## [1.1.0] - 2026-09-16
### Adicionado
- Proteção anti-CSRF (cookie de validação HttpOnly + cookie legível +
  header `X-CSRF-TOKEN`) em toda rota que muda estado, tela e API —
  conferida automaticamente, sem precisar decorar cada action.
- Cabeçalhos de segurança em toda resposta: `Content-Security-Policy`,
  `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`,
  `Permissions-Policy`.
- Rate limiting por IP nas rotas de login, registro, recuperação de
  senha e emissão de nota mensal em lote, mais um limite global de
  rede de segurança.
- Retry com backoff exponencial e circuit breaker (Polly) nas chamadas
  à SEFIN Nacional e ao ADN — uma instabilidade do lado deles não trava
  mais uma emissão em lote inteira.

### Alterado
- Todo JavaScript e CSS que estava inline nas views foi movido para
  arquivos externos em `wwwroot/`, permitindo uma
  `Content-Security-Policy` sem `'unsafe-inline'` em `script-src`.

## [1.0.1] - 2026-09-15
### Corrigido
- Descrição do serviço na emissão avulsa de Nfse era um campo de uma
  linha só, mesmo aceitando até 2000 caracteres; agora é um textarea.
- Contrato podia ser salvo com uma linha de Serviço vazia, quebrando o
  model binding no backend e devolvendo um 400 sem log nenhum; agora é
  barrado no formulário antes de enviar.
- NfseApiClient não logava o corpo da resposta quando a SEFIN rejeitava
  a emissão da DPS (só o cancelamento já fazia isso).
- DpsBuilder mandava `pTotTribSN` vazio quando a Empresa não tinha o
  percentual preenchido, quebrando o XSD da SEFIN (E1235); agora omite
  o campo quando não há valor.
- Cadastro de Empresa não exigia o percentual do Simples Nacional para
  MEI (só para ME/EPP), permitindo salvar cadastro de Optante incompleto.
- `dhEmi` da DPS era montado no fuso do servidor (UTC em produção) em
  vez de Brasília fixo, causando rejeição E0008 na SEFIN.

## [1.0.0] - 2026-09-12
### Adicionado
- Tela "Logs do sistema" (`/Logs`) — visualização dos logs de erro direto
  na UI, sem precisar de terminal/SSH. Restrita ao grupo Administrador.
- Tela "Sobre" (`/Sobre`) — versão em produção e histórico de mudanças
  visível pro Administrador.

### Corrigido
- Exceções não tratadas em produção não eram logadas (middleware de log
  nunca era alcançado — `UseExceptionHandler` engolia a exceção antes).
  Agora toda exceção é logada com stack trace completo e `CorrelationId`.
- Tela quebrada exibia JSON cru ou 404 em branco; agora mostra uma
  página de erro amigável com código de correlação pro suporte.

## [1.3.0] - 2026-09-16
### Adicionado
- Catálogo oficial de Código de Tributação Nacional (cTribNac, 338
  códigos) e de NBS — Nomenclatura Brasileira de Serviços (920
  códigos), alimentados a partir das tabelas oficiais do governo.
- Busca com autocomplete nos campos cTribNac e NBS do cadastro de
  Serviço, no lugar de digitação livre.

### Alterado
- `CodigoTributacaoNacional` e `CodigoNbs` do Serviço agora são
  validados contra o catálogo oficial antes de salvar (não só
  formato) — bloqueia código que a SEFIN rejeitaria de qualquer jeito.

### Corrigido
- Busca nos catálogos não encontrava termos que só apareciam com
  letra maiúscula na descrição (comparação estava case-sensitive).

## [1.4.0] - 2026-09-16
### Adicionado
- Campo Cliente com busca por CNPJ/CPF ou nome (autocomplete) nos
  modais de cadastro de Contrato e de emissão avulsa de Nfse.

### Alterado
- Modal de cadastro de Empresa reorganizado em abas (Dados gerais,
  Endereço, Regime tributário, Contratos), no lugar de uma coluna só.
- Campos de reajuste do Contrato (periodicidade, índice, alerta de
  antecedência) agora dependem do Tipo de cobrança: obrigatórios e
  editáveis para Mensal, zerados e bloqueados para Avulso — validado
  também no servidor.

### Corrigido
- Salvar um Contrato ou Empresa com campo obrigatório vazio numa aba
  diferente da atual não avisava nada (o navegador barra o envio sem
  mostrar aviso quando o campo inválido está escondido). Agora troca
  pra aba certa e mostra qual campo falta.
- Campo Cliente na emissão avulsa de nota carregava a lista inteira
  (até 200 clientes) e vinha com um selecionado sem o usuário escolher.