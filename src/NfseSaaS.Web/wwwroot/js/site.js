/**
 * Botão de mostrar/ocultar senha — padrão único em todo campo de senha
 * do sistema (Login, Registrar, Convite, Esqueci/Redefinir senha, modal
 * de Alterar senha). Delegado no document (não por elemento) de
 * propósito: funciona também em conteúdo injetado depois, como o modal
 * de Alterar senha em _Layout.cshtml.
 */
document.addEventListener('click', function (e) {
    const botao = e.target.closest('.btn-toggle-senha');
    if (!botao) return;

    const input = botao.previousElementSibling;
    if (!input || input.tagName !== 'INPUT') return;

    const icone = botao.querySelector('i');
    if (input.type === 'password') {
        input.type = 'text';
        if (icone) icone.classList.replace('bi-eye', 'bi-eye-slash');
    } else {
        input.type = 'password';
        if (icone) icone.classList.replace('bi-eye-slash', 'bi-eye');
    }
});

/** Lê o valor de um cookie pelo nome (usado só pro token anti-CSRF, abaixo). */
function lerCookie(nome) {
    const prefixo = `${nome}=`;
    const linha = document.cookie.split('; ').find(l => l.startsWith(prefixo));
    return linha ? decodeURIComponent(linha.substring(prefixo.length)) : null;
}

/**
 * Header anti-CSRF a incluir em toda requisição que muda estado
 * (POST/PUT/DELETE) — o backend confere este header contra um cookie
 * HttpOnly separado em ValidacaoAntiforgeryFilter (Program.cs/Filters/).
 * O cookie "NfseSaaS.Xsrf-Token" (legível por JS) é emitido pelo próprio
 * backend em toda resposta, então já está presente antes do primeiro
 * fetch — inclusive na tela de login, sem precisar de sessão.
 */
function obterCsrfHeader() {
    const token = lerCookie('NfseSaaS.Xsrf-Token');
    return token ? { 'X-CSRF-TOKEN': token } : {};
}

/**
 * Helper único de chamadas à API JSON (mesma API que Swagger/clients
 * externos consomem — as telas MVC nunca duplicam lógica de negócio,
 * só chamam /api/... via fetch). Centralizado aqui porque toda tela
 * (Empresas, Clientes, Serviços, Nfse...) repete o mesmo tratamento de
 * 401 (sessão expirou → login) e 400/422 (erro de validação/regra de
 * negócio → mostrar mensagem).
 */
async function apiFetch(url, options) {
    const resposta = await fetch(url, {
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
        ...options
    });

    if (resposta.status === 401) {
        window.location.href = '/Account/Login?returnUrl=' + encodeURIComponent(window.location.pathname);
        throw new Error('Sessão expirada.');
    }

    if (resposta.status === 403) {
        // Nova versão dos Termos de uso/Política de privacidade ainda não
        // aceita (ExigeAceiteTermosFilter) — leva para a tela de aceite.
        const corpo = await resposta.json().catch(() => null);
        if (corpo?.codigo === 'termos_pendentes') {
            window.location.href = (corpo.url || '/Legal/Aceitar') + '?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
            throw new Error(corpo.erro);
        }
        throw new Error('Você não tem permissão para esta ação.');
    }

    if (!resposta.ok) {
        const corpo = await resposta.json().catch(() => null);
        throw new Error(extrairMensagemDeErro(corpo) ?? `Erro ${resposta.status}.`);
    }

    if (resposta.status === 204) return null;

    return await resposta.json().catch(() => null);
}

/**
 * Desabilita o botão clicado enquanto a ação assíncrona associada a ele
 * roda, e reabilita no fim (sucesso ou erro). Usado no handler de clique
 * delegado de toda tabela de listagem (Empresas, Clientes, Serviços,
 * Contratos, Usuários, Grupos, E-mails) — sem isso, dava pra clicar
 * "ativar/desativar" (ou excluir, editar etc.) várias vezes seguidas
 * antes da primeira requisição terminar e do DataTable redesenhar a
 * linha, disparando uma requisição nova a cada clique. `if (botao.disabled)
 * return` cobre o clique duplo mais rápido que o navegador consegue
 * disparar (entre o clique e o disabled=true não há await no meio).
 */
async function executarComBotaoDesabilitado(botao, acaoAsync) {
    if (botao.disabled) return;

    botao.disabled = true;
    try {
        await acaoAsync();
    } finally {
        botao.disabled = false;
    }
}

/**
 * Envio de arquivo (multipart) com andamento visível: o botão vira um
 * spinner com "Enviando 45%" e, quando o arquivo termina de subir,
 * "Processando…" até o servidor responder. `barra` (opcional) é um
 * `.progress` com uma `.progress-bar` dentro. XMLHttpRequest em vez de
 * fetch porque só ele informa o progresso do envio. Devolve o corpo JSON
 * da resposta (ou null); erro vira Error com a mensagem da API.
 */
function enviarArquivoComProgresso(url, dados, opcoes) {
    const { metodo = 'POST', botao = null, barra = null } = opcoes ?? {};
    const preenchimento = barra?.querySelector('.progress-bar');
    const ehRotulo = botao?.tagName === 'LABEL';
    const htmlOriginal = botao?.innerHTML;

    function mostrar(texto, percentual) {
        if (botao) botao.innerHTML = `<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> ${texto}`;
        if (!preenchimento) return;
        const processando = percentual == null;
        preenchimento.classList.toggle('progress-bar-striped', processando);
        preenchimento.classList.toggle('progress-bar-animated', processando);
        preenchimento.style.width = `${processando ? 100 : percentual}%`;
        barra.setAttribute('aria-valuenow', String(processando ? 100 : percentual));
    }

    function restaurar() {
        if (botao) {
            botao.innerHTML = htmlOriginal;
            botao.style.minWidth = '';
            if (ehRotulo) { botao.classList.remove('disabled'); botao.removeAttribute('aria-disabled'); }
            else botao.disabled = false;
        }
        if (barra) barra.hidden = true;
    }

    if (botao) {
        botao.style.minWidth = `${botao.offsetWidth}px`; // o texto muda; o botão não "pula"
        if (ehRotulo) { botao.classList.add('disabled'); botao.setAttribute('aria-disabled', 'true'); }
        else botao.disabled = true;
    }
    if (barra) barra.hidden = false;
    mostrar('Enviando…', 0);

    return new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open(metodo, url);
        xhr.withCredentials = true;
        Object.entries(obterCsrfHeader()).forEach(([nome, valor]) => xhr.setRequestHeader(nome, valor));
        xhr.setRequestHeader('Accept', 'application/json');

        xhr.upload.addEventListener('progress', e => {
            if (!e.lengthComputable) return;
            const percentual = Math.round(e.loaded * 100 / e.total);
            if (percentual < 100) mostrar(`Enviando ${percentual}%`, percentual);
            else mostrar('Processando…', null);
        });
        xhr.upload.addEventListener('load', () => mostrar('Processando…', null));

        xhr.addEventListener('load', () => {
            restaurar();
            let corpo = null;
            try { corpo = xhr.responseText ? JSON.parse(xhr.responseText) : null; } catch { /* resposta sem JSON (ex.: erro do proxy) */ }
            if (xhr.status >= 200 && xhr.status < 300) resolve(corpo);
            else reject(new Error(extrairMensagemDeErro(corpo) ?? mensagemErroEnvioArquivo(xhr.status)));
        });
        xhr.addEventListener('error', () => { restaurar(); reject(new Error('Não foi possível falar com o servidor. Confira a internet e tente de novo.')); });
        xhr.addEventListener('abort', () => { restaurar(); reject(new Error('O envio foi interrompido. Tente de novo.')); });
        xhr.send(dados);
    });
}

/** Mensagem para envio recusado sem explicação da API (ex.: o Nginx barra pelo tamanho antes do sistema). */
function mensagemErroEnvioArquivo(status) {
    if (status === 413)
        return 'O servidor recusou o arquivo por ser grande demais para o limite dele. Se o arquivo está dentro do tamanho indicado na tela, avise o suporte.';
    if (status === 401 || status === 403)
        return 'Sua sessão expirou ou você não tem permissão para enviar este arquivo. Entre de novo e tente outra vez.';
    return `Erro ${status} ao enviar o arquivo. Tente de novo.`;
}

/**
 * A API tem 2 formatos de erro possíveis (ver ExceptionHandlingMiddleware
 * e os poucos BadRequest/Conflict manuais em AuthController):
 * - { erro: "...", erros?: { campo: ["msg", ...] } } — formato padrão
 * - ["msg1", "msg2"] — erros de senha/Identity (UserManager.CreateAsync)
 */
function extrairMensagemDeErro(corpo) {
    if (!corpo) return null;
    if (Array.isArray(corpo)) return corpo.join(' ');
    if (corpo.erros) {
        const porCampo = Object.values(corpo.erros).flat().join(' ');
        return corpo.erro ? `${corpo.erro} ${porCampo}` : porCampo;
    }
    if (corpo.erro) return corpo.erro;
    return null;
}

function mostrarErro(mensagem) {
    mostrarToast(mensagem, 'erro');
}

/**
 * Toast discreto no canto da tela — substitui o alert() nativo do
 * navegador (mais profissional, não bloqueia a interação). tipo:
 * 'sucesso' (padrão) ou 'erro' (fundo vermelho). Empilha: cada chamada
 * cria um toast novo e independente — um toast que já está na tela não
 * é sobrescrito por uma chamada seguinte, como acontecia antes (só
 * havia um elemento fixo, reaproveitado a cada mostrarToast()).
 */
function mostrarToast(mensagem, tipo) {
    const container = document.getElementById('toastContainerNfse');
    if (!container) {
        // Fallback pras páginas que ainda não usam o _Layout novo.
        alert(mensagem);
        return;
    }

    const ehErro = tipo === 'erro';
    // Erro longo (ex.: rejeição da SEFIN com "o que fazer") fica mais
    // tempo na tela — dá para ler antes de sumir (máx. 15 s).
    const duracaoMs = ehErro ? Math.min(15000, Math.max(5000, String(mensagem).length * 60)) : 3000;

    const toast = document.createElement('div');
    toast.className = 'toast-nfse' + (ehErro ? ' toast-erro' : '');
    toast.innerHTML = `
        <i class="bi ${ehErro ? 'bi-exclamation-triangle-fill' : 'bi-check-circle-fill'} toast-nfse-icone"></i>
        <span class="toast-nfse-texto"></span>
        <button type="button" class="toast-nfse-fechar" aria-label="Fechar"><i class="bi bi-x-lg"></i></button>
        <div class="toast-nfse-barra" style="animation-duration: ${duracaoMs}ms"></div>
    `;
    // .textContent, não interpolado no innerHTML acima — mensagem pode
    // vir de erro de API/validação, nunca confiar nela como HTML.
    const contato = ehErro ? contatoSuporteParaMensagem(mensagem) : '';
    toast.querySelector('.toast-nfse-texto').textContent = contato ? `${mensagem} Suporte: ${contato}.` : mensagem;

    function remover() {
        toast.classList.remove('show');
        toast.classList.add('hide');
        toast.addEventListener('transitionend', () => toast.remove(), { once: true });
    }

    const timer = setTimeout(remover, duracaoMs);
    toast.querySelector('.toast-nfse-fechar').addEventListener('click', function () {
        clearTimeout(timer);
        remover();
    });

    container.appendChild(toast);
    // Força o navegador a aplicar o estado inicial (opacity/transform
    // de "fora da tela") antes de adicionar .show — sem isto as duas
    // mudanças caem no mesmo frame e a transição de entrada não anima.
    void toast.offsetWidth;
    toast.classList.add('show');
}

/**
 * ---- Padrão único de feedback do projeto ----
 *
 * 3 categorias, cada uma com um motivo pra existir separada — não dá
 * pra jogar tudo num toast só porque "padronizar" não é a mesma coisa
 * que "usar 1 componente pra tudo":
 *
 * 1. mostrarToast() — mensagem rápida e solta (sucesso de uma ação,
 *    resultado de uma consulta). Não trava nada, desaparece só.
 * 2. mostrarErroFormulario()/ocultarErroFormulario() — erro de
 *    validação/regra de negócio ao SALVAR um formulário dentro de um
 *    modal aberto. Fica fixo dentro do próprio modal, de propósito: se
 *    fosse toast, o usuário podia nem ver (o modal cobre a tela) e
 *    perderia a referência de qual campo revisar.
 * 3. mostrarResultado()/ocultarResultado() — resultado persistente
 *    associado a uma ação dentro de um modal (ex.: teste de conexão de
 *    certificado). Mesma lógica do item 2: fica ali, associado ao
 *    botão que o usuário apertou.
 * 4. confirmarAcao() — substitui o confirm() nativo do navegador (que
 *    não combina com o resto da tela) por um modal Bootstrap
 *    consistente. Retorna uma Promise<boolean>.
 */

/**
 * Contato do suporte (configuração "Suporte", vem no data-suporte do
 * <body>) para completar mensagens de erro que dizem "fale com o suporte"
 * (cobrança, conta suspensa, cadastro fechado...). Vazio = sem contato.
 */
function contatoSuporteParaMensagem(mensagem) {
    const contato = document.body?.dataset?.suporte;
    if (!contato || !/suporte/i.test(String(mensagem ?? ''))) return '';
    return contato;
}

function mostrarErroFormulario(idDiv, mensagem) {
    const div = document.getElementById(idDiv);
    if (!div) return;
    const contato = contatoSuporteParaMensagem(mensagem);
    div.textContent = contato ? `${mensagem} Suporte: ${contato}.` : mensagem;
    div.classList.remove('d-none');
}

function ocultarErroFormulario(idDiv) {
    const div = document.getElementById(idDiv);
    if (!div) return;
    div.classList.add('d-none');
}

/**
 * Confere se o <form> passa na validação nativa (required, etc.) antes
 * de deixar o submit prosseguir. Feito pra formulário com abas
 * (Bootstrap tab-pane escondido via d-none): sem isto, quando o campo
 * inválido está numa aba não visível, o navegador bloqueia o submit
 * SILENCIOSAMENTE — não dá pra mostrar o balão de aviso nativo em cima
 * de um elemento escondido, e o evento 'submit' nem chega a disparar.
 * Por isso o <form> precisa do atributo `novalidate` (ver
 * Views/Contratos e Views/Empresas) — desliga o bloqueio automático do
 * navegador, e a checagem passa a ser feita aqui, manualmente, já
 * dentro do nosso handler de submit.
 *
 * seletorPainel: seletor CSS do container de cada aba (ex.:
 * '.tab-conteudo-contrato'). trocarAba: a função de troca de aba já
 * existente na tela (trocarAbaContrato, trocarAbaEmpresa) — recebe o
 * data-tab do painel onde está o primeiro campo inválido.
 */
function validarFormularioComAbas(form, seletorPainel, trocarAba, idErroDiv) {
    const primeiroInvalido = form.querySelector(':invalid');
    if (!primeiroInvalido) return true;

    const painel = primeiroInvalido.closest(seletorPainel);
    if (painel) trocarAba(painel.dataset.tab);

    // Rótulo pelo "for" (campo dentro de input-group também acha); senão, o do bloco.
    const rotulo = (primeiroInvalido.id && form.querySelector(`label.form-label[for="${primeiroInvalido.id}"]`))
        || primeiroInvalido.closest('div')?.querySelector('label.form-label');
    const nomeCampo = rotulo ? rotulo.textContent.trim() : (primeiroInvalido.name || primeiroInvalido.id || 'um campo');

    mostrarErroFormulario(idErroDiv, `Preencha "${nomeCampo}" antes de salvar.`);
    primeiroInvalido.focus();
    return false;
}

function mostrarResultado(idDiv, sucesso, mensagem) {
    const div = document.getElementById(idDiv);
    if (!div) return;
    div.className = `alert ${sucesso ? 'alert-success' : 'alert-danger'} mt-3`;
    div.textContent = mensagem;
    div.classList.remove('d-none');
}

/**
 * Quadro de resultado (ex.: "Importar agora", "Verificar reajustes") com X
 * para fechar. Sucesso fecha sozinho depois de `segundos` (padrão 10), com
 * contador "fecha em Ns" e uma barra que encolhe — e pausa enquanto o mouse
 * está em cima, pra dar tempo de ler. Aviso/erro NÃO fecha sozinho: quem
 * precisa agir não pode perder a mensagem. `html` já deve vir escapado.
 */
function mostrarQuadroResultado(idDiv, html, tipo, segundos) {
    const div = document.getElementById(idDiv);
    if (!div) return;

    if (div._quadroTimer) clearInterval(div._quadroTimer);
    div._quadroTimer = null;

    const autoFechar = tipo === 'success';
    const total = segundos ?? 10;
    div.className = `alert alert-${tipo} quadro-resultado position-relative pe-5`;
    div.innerHTML = `
        <button type="button" class="btn-close position-absolute top-0 end-0 m-2" aria-label="Fechar"></button>
        <div>${html}</div>
        ${autoFechar ? `<div class="small text-muted mt-2 quadro-resultado-contador">fecha em ${total}s…</div>
                        <div class="quadro-resultado-barra" style="width:100%"></div>` : ''}`;

    function fechar() {
        if (div._quadroTimer) clearInterval(div._quadroTimer);
        div._quadroTimer = null;
        div.classList.add('d-none');
        div.innerHTML = '';
    }

    div.querySelector('.btn-close').addEventListener('click', fechar);
    if (!autoFechar) return;

    let restante = total * 10; // décimos de segundo — barra suave
    let pausado = false;
    div.onmouseenter = () => { pausado = true; };
    div.onmouseleave = () => { pausado = false; };

    const contador = div.querySelector('.quadro-resultado-contador');
    const barra = div.querySelector('.quadro-resultado-barra');
    div._quadroTimer = setInterval(() => {
        if (pausado) {
            contador.textContent = 'pausado — tire o mouse para continuar';
            return;
        }
        restante--;
        contador.textContent = `fecha em ${Math.ceil(restante / 10)}s…`;
        barra.style.width = `${(restante / (total * 10)) * 100}%`;
        if (restante <= 0) fechar();
    }, 100);
}

/**
 * Marca cada seção de um modal com ✓ (completa) ou ! (falta campo
 * obrigatório), olhando os campos :invalid de cada painel. painelSeletor:
 * os painéis (com data-tab); navSeletor: o container dos botões
 * (data-tab-alvo) com um .secao-status dentro.
 */
function atualizarStatusSecoes(formId, painelSeletor, navSeletor) {
    const form = document.getElementById(formId);
    if (!form) return;
    form.querySelectorAll(painelSeletor).forEach(painel => {
        const botao = document.querySelector(`${navSeletor} [data-tab-alvo="${painel.dataset.tab}"]`);
        const status = botao?.querySelector('.secao-status');
        if (!status) return;
        const temObrigatorio = painel.querySelector('[required]') !== null;
        const pendente = painel.querySelector(':invalid') !== null;
        status.className = 'secao-status' + (!temObrigatorio ? '' : pendente ? ' pendente' : ' ok');
        status.textContent = !temObrigatorio ? '' : pendente ? '!' : '';
        status.title = !temObrigatorio ? '' : pendente ? 'Falta preencher campo obrigatório' : 'Completa';
    });
}

/** Mostra "Salvador — BA" abaixo do código IBGE (tabela local de municípios). */
async function mostrarNomeMunicipio(idCodigo, idRotulo) {
    const rotulo = document.getElementById(idRotulo);
    const codigo = (document.getElementById(idCodigo)?.value ?? '').replace(/\D/g, '');
    if (!rotulo) return;
    rotulo.classList.remove('text-danger');

    if (codigo.length !== 7) {
        rotulo.textContent = 'Preenchido pelo CEP (código IBGE de 7 dígitos).';
        return;
    }

    try {
        const m = await apiFetch(`/api/consultas/municipio/${codigo}`);
        rotulo.innerHTML = `<i class="bi bi-geo-alt-fill text-primary"></i> <strong>${escaparHtml(m.nome)}${m.uf ? ' — ' + escaparHtml(m.uf) : ''}</strong>`;
    } catch (err) {
        // Tabela local do IBGE (C9): código que não existe é erro de cadastro.
        rotulo.textContent = err.message || `Código IBGE ${codigo} não encontrado.`;
        rotulo.classList.add('text-danger');
    }
}

/**
 * Busca do município pelo nome (C9), num <select> com select2: ao escolher,
 * preenche o código IBGE e a UF. ids: { busca, codigoMunicipio, uf,
 * nomeMunicipio, modal }.
 */
function configurarBuscaMunicipio(ids) {
    const $busca = $('#' + ids.busca);
    if (!$busca.length) return;

    $busca.select2({
        dropdownParent: ids.modal ? $('#' + ids.modal) : undefined,
        placeholder: 'Ou busque pelo nome: Salvador, São Paulo/SP…',
        allowClear: true,
        minimumInputLength: 2,
        width: '100%',
        language: {
            inputTooShort: () => 'Digite pelo menos 2 letras do município.',
            searching: () => 'Buscando…',
            noResults: () => 'Nenhum município encontrado.'
        },
        ajax: {
            url: '/api/catalogos/municipios',
            dataType: 'json',
            delay: 250,
            data: params => ({ busca: params.term, limite: 20 }),
            processResults: data => ({ results: data.results })
        }
    });

    $busca.on('select2:select', async e => {
        const m = e.params.data;
        const codigo = document.getElementById(ids.codigoMunicipio);
        codigo.value = m.id;
        if (ids.uf && m.uf) document.getElementById(ids.uf).value = m.uf;
        codigo.dispatchEvent(new Event('input', { bubbles: true }));
        await mostrarNomeMunicipio(ids.codigoMunicipio, ids.nomeMunicipio);
        $busca.val(null).trigger('change');
    });
}

/**
 * C2: select2 de município (local da prestação). Vazio = município da
 * empresa. ids: { select, modal, placeholder }.
 */
function configurarSelectMunicipio(ids) {
    $('#' + ids.select).select2({
        dropdownParent: ids.modal ? $('#' + ids.modal) : undefined,
        placeholder: ids.placeholder ?? 'Município da empresa',
        allowClear: true,
        minimumInputLength: 2,
        width: '100%',
        language: {
            inputTooShort: () => 'Digite pelo menos 2 letras do município.',
            searching: () => 'Buscando…',
            noResults: () => 'Nenhum município encontrado.'
        },
        ajax: {
            url: '/api/catalogos/municipios',
            dataType: 'json',
            delay: 250,
            data: params => ({ busca: params.term, limite: 20 }),
            processResults: data => ({ results: data.results })
        }
    });
}

/** Põe um município (pelo código) no select2 de município; sem código, limpa. */
async function definirMunicipioNoSelect(idSelect, codigo) {
    const $select = $('#' + idSelect);
    $select.empty();
    if (!codigo) {
        $select.val(null).trigger('change');
        return;
    }
    let texto = codigo;
    try {
        const m = await apiFetch(`/api/consultas/municipio/${codigo}`);
        texto = `${m.nome}${m.uf ? '/' + m.uf : ''} (${codigo})`;
    } catch {
        // Sem o nome, fica o código (a emissão confere de novo).
    }
    $select.append(new Option(texto, codigo, true, true)).trigger('change');
}

/** Nome do município selecionado (texto do select2) ou o padrão. */
function textoMunicipioSelecionado(idSelect, padrao) {
    const dado = $('#' + idSelect).select2('data')[0];
    return dado?.id ? dado.text : padrao;
}

/**
 * CEP → preenche logradouro, bairro, UF e código IBGE do município
 * (ViaCEP, via /api/consultas/cep). ids: { cep, logradouro, bairro,
 * complemento, uf, codigoMunicipio, nomeMunicipio, statusCep, numero,
 * botao, aoPreencher }. Busca sozinho ao completar 8 dígitos; o botão
 * (lupa) busca de novo mesmo com o CEP já consultado — para corrigir um
 * endereço digitado errado.
 */
function configurarBuscaCep(ids) {
    const campoCep = document.getElementById(ids.cep);
    const campoCodigo = document.getElementById(ids.codigoMunicipio);
    const status = document.getElementById(ids.statusCep);
    if (!campoCep) return;

    let ultimoBuscado = null;

    async function buscar(forcar) {
        const cep = campoCep.value.replace(/\D/g, '');
        if (forcar === true && cep.length !== 8) {
            if (status) { status.textContent = 'Digite os 8 dígitos do CEP.'; status.classList.add('text-danger'); }
            campoCep.focus();
            return;
        }
        if (cep.length !== 8 || (cep === ultimoBuscado && forcar !== true)) return;
        ultimoBuscado = cep;

        if (status) { status.classList.remove('text-danger'); status.textContent = 'Buscando endereço…'; }
        try {
            const e = await apiFetch(`/api/consultas/cep/${cep}`);
            const definir = (id, valor) => { const c = document.getElementById(id); if (c && valor) c.value = valor; };
            definir(ids.logradouro, e.logradouro);
            definir(ids.bairro, e.bairro);
            definir(ids.uf, e.uf);
            definir(ids.codigoMunicipio, e.codigoMunicipio);
            const complemento = document.getElementById(ids.complemento);
            if (complemento && !complemento.value && e.complemento) complemento.value = e.complemento;

            if (status) status.textContent = `${e.municipio} — ${e.uf}`;
            await mostrarNomeMunicipio(ids.codigoMunicipio, ids.nomeMunicipio);
            if (ids.numero) document.getElementById(ids.numero)?.focus();
            ids.aoPreencher?.();
        } catch (err) {
            ultimoBuscado = null;
            if (status) { status.textContent = err.message; status.classList.add('text-danger'); }
        }
    }

    // Aceita colar "40170-120": guarda só os dígitos (a API espera 8).
    campoCep.addEventListener('input', () => {
        const digitos = campoCep.value.replace(/\D/g, '').slice(0, 8);
        if (campoCep.value !== digitos) campoCep.value = digitos;
        if (digitos.length === 8) buscar();
    });
    campoCep.addEventListener('blur', () => buscar());
    if (ids.botao) {
        const botao = document.getElementById(ids.botao);
        botao?.addEventListener('click', () => executarComBotaoDesabilitado(botao, () => buscar(true)));
    }

    campoCodigo?.addEventListener('change', () => mostrarNomeMunicipio(ids.codigoMunicipio, ids.nomeMunicipio));

    return { reiniciar: () => { ultimoBuscado = null; if (status) { status.textContent = ''; status.classList.remove('text-danger'); } } };
}

function ocultarResultado(idDiv) {
    const div = document.getElementById(idDiv);
    if (!div) return;
    div.classList.add('d-none');
}

/**
 * Data de HOJE no fuso local, formato YYYY-MM-DD (pra <input type="date">
 * ou nomes de arquivo). NUNCA usar `new Date().toISOString().slice(0,10)`
 * pra isso — toISOString() converte pra UTC, e à noite no horário de
 * Brasília (UTC-3) isso já vira o dia seguinte. Foi exatamente esse bug
 * que fez uma DataCompetencia de emissão vir como "amanhã" e a SEFIN
 * rejeitar a nota (a competência não pode ser posterior à emissão).
 */
function dataLocalIso(data) {
    data = data || new Date();
    const ano = data.getFullYear();
    const mes = String(data.getMonth() + 1).padStart(2, '0');
    const dia = String(data.getDate()).padStart(2, '0');
    return `${ano}-${mes}-${dia}`;
}

/**
 * Escapa texto pra interpolar com segurança em HTML montado por string
 * (template literal + innerHTML / render do DataTables) — dado vindo do
 * cadastro (nome, apelido) pode ter <, &, aspas.
 */
// Como o contrato aparece em títulos e combos: "Contrato 2026/0007 — Folha filial"
// (mesma regra de NumeroContratoRegras.Rotulo no servidor).
function rotuloContrato(c) {
    const numero = (c?.numero ?? '').trim();
    const descricao = (c?.descricao ?? '').trim();
    if (numero && descricao) return `Contrato ${numero} — ${descricao}`;
    if (numero) return `Contrato ${numero}`;
    return descricao || 'Contrato';
}

// WhatsApp (celular do Brasil): máscara "(71) 99999-8888" enquanto digita.
// A conferência de verdade é no servidor (RegrasWhatsApp).
function aplicarMascaraWhatsApp(campo) {
    if (!campo) return;
    campo.addEventListener('input', function () {
        let d = campo.value.replace(/\D/g, '');
        if (d.length > 11 && d.startsWith('55')) d = d.slice(2);
        d = d.slice(0, 11);
        campo.value = d.length <= 2 ? (d ? `(${d}` : '')
            : d.length <= 7 ? `(${d.slice(0, 2)}) ${d.slice(2)}`
            : `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
    });
}

function escaparHtml(texto) {
    return String(texto ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function confirmarAcao(mensagem, opcoes) {
    opcoes = opcoes || {};
    return new Promise(function (resolve) {
        const modalEl = document.getElementById('modalConfirmacao');
        if (!modalEl || typeof bootstrap === 'undefined') {
            // Fallback pras páginas que ainda não usam o _Layout novo.
            resolve(confirm(mensagem));
            return;
        }

        document.getElementById('modalConfirmacaoTitulo').textContent = opcoes.titulo || 'Confirmar ação';
        document.getElementById('modalConfirmacaoMensagem').textContent = mensagem;

        const botaoConfirmar = document.getElementById('modalConfirmacaoBotaoConfirmar');
        botaoConfirmar.textContent = opcoes.textoBotao || 'Confirmar';
        botaoConfirmar.className = 'btn ' + (opcoes.variante === 'perigo' ? 'btn-danger' : 'btn-primary');

        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        let confirmado = false;

        function aoConfirmar() {
            confirmado = true;
            modal.hide();
        }
        function aoFechar() {
            botaoConfirmar.removeEventListener('click', aoConfirmar);
            modalEl.removeEventListener('hidden.bs.modal', aoFechar);
            resolve(confirmado);
        }

        botaoConfirmar.addEventListener('click', aoConfirmar);
        modalEl.addEventListener('hidden.bs.modal', aoFechar, { once: true });
        modal.show();
    });
}

/**
 * Modal global "Alterar senha" (ver _Layout.cshtml) — acionado pelo
 * dropdown do usuário em qualquer tela do sistema, por isso a lógica
 * mora aqui em vez de num arquivo por página.
 */
document.addEventListener('DOMContentLoaded', function () {
    const modalEl = document.getElementById('modalAlterarSenha');
    const form = document.getElementById('form-alterar-senha-modal');
    if (!modalEl || !form) return; // páginas que não usam _Layout.cshtml (ex.: telas de login/registro)

    modalEl.addEventListener('show.bs.modal', function () {
        form.reset();
        document.getElementById('erro-senha-modal').classList.add('d-none');
        document.getElementById('sucesso-senha-modal').classList.add('d-none');
    });

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        const erroDiv = document.getElementById('erro-senha-modal');
        const sucessoDiv = document.getElementById('sucesso-senha-modal');
        erroDiv.classList.add('d-none');
        sucessoDiv.classList.add('d-none');

        const novaSenha = document.getElementById('novaSenhaModal').value;
        const confirmarNovaSenha = document.getElementById('confirmarNovaSenhaModal').value;

        if (novaSenha !== confirmarNovaSenha) {
            erroDiv.textContent = 'A confirmação não bate com a nova senha.';
            erroDiv.classList.remove('d-none');
            return;
        }

        try {
            await apiFetch('/api/auth/alterar-senha', {
                method: 'POST',
                body: JSON.stringify({
                    senhaAtual: document.getElementById('senhaAtualModal').value,
                    novaSenha
                })
            });

            sucessoDiv.textContent = 'Senha alterada com sucesso.';
            sucessoDiv.classList.remove('d-none');
            form.reset();
        } catch (err) {
            erroDiv.textContent = err.message;
            erroDiv.classList.remove('d-none');
        }
    });
});

/**
 * Verificação em duas etapas (B3) — modal do menu do usuário, presente
 * em todas as telas do _Layout. Ligar: código por e-mail; desligar: senha.
 */
document.addEventListener('DOMContentLoaded', function () {
    const modalEl = document.getElementById('modalDuploFator');
    if (!modalEl) return;

    const erro = document.getElementById('erro-duplo-fator');
    const sucesso = document.getElementById('sucesso-duplo-fator');
    const blocoDesligado = document.getElementById('duplo-fator-desligado');
    const blocoLigado = document.getElementById('duplo-fator-ligado');
    const formAtivar = document.getElementById('form-duplo-fator-ativar');

    function mensagem(div, texto) {
        erro.classList.add('d-none');
        sucesso.classList.add('d-none');
        if (!div) return;
        div.textContent = texto;
        div.classList.remove('d-none');
    }

    async function carregar() {
        const situacao = await apiFetch('/api/auth/duplo-fator');
        document.getElementById('email-duplo-fator').textContent = situacao.emailMascarado;
        blocoLigado.classList.toggle('d-none', !situacao.ligado);
        blocoDesligado.classList.toggle('d-none', situacao.ligado);
        formAtivar.classList.add('d-none');
        formAtivar.reset();
        document.getElementById('form-duplo-fator-desligar').reset();
    }

    modalEl.addEventListener('show.bs.modal', async function () {
        mensagem(null);
        try {
            await carregar();
        } catch (err) {
            mensagem(erro, err.message);
        }
    });

    document.getElementById('btn-duplo-fator-enviar').addEventListener('click', async function () {
        mensagem(null);
        try {
            const r = await apiFetch('/api/auth/duplo-fator/codigo', { method: 'POST' });
            mensagem(sucesso, `Enviamos um código para ${r.email}. Digite-o abaixo.`);
            formAtivar.classList.remove('d-none');
            document.getElementById('codigo-duplo-fator').focus();
        } catch (err) {
            mensagem(erro, err.message);
        }
    });

    formAtivar.addEventListener('submit', async function (e) {
        e.preventDefault();
        const codigo = document.getElementById('codigo-duplo-fator').value.replace(/\D/g, '');
        if (codigo.length !== 6) {
            mensagem(erro, 'O código tem 6 números.');
            return;
        }
        try {
            await apiFetch('/api/auth/duplo-fator/ativar', { method: 'POST', body: JSON.stringify({ codigo }) });
            await carregar();
            mensagem(sucesso, 'Verificação em duas etapas ligada. No próximo login, o código será pedido.');
        } catch (err) {
            mensagem(erro, err.message);
        }
    });

    document.getElementById('form-duplo-fator-desligar').addEventListener('submit', async function (e) {
        e.preventDefault();
        try {
            await apiFetch('/api/auth/duplo-fator/desligar', {
                method: 'POST',
                body: JSON.stringify({ senha: document.getElementById('senha-duplo-fator').value })
            });
            await carregar();
            mensagem(sucesso, 'Verificação em duas etapas desligada.');
        } catch (err) {
            mensagem(erro, err.message);
        }
    });
});

/**
 * Campo "Modelo do e-mail da nota" (Empresa, Cliente): carrega os modelos
 * (cadastro Modelos de e-mail) num <select>. A primeira opção (valor vazio)
 * é "não escolher" — na Empresa, o padrão da conta; no Cliente, o da
 * empresa. O padrão não aparece de novo na lista: escolher "vazio" já o usa.
 * Guarda o valor carregado em data-original (para só salvar se mudou).
 */
async function preencherSelectModeloEmail(select, valorAtual, rotuloSemModelo) {
    let opcoes = [];
    try {
        opcoes = await apiFetch('/api/modelos-email-nota/opcoes');
    } catch {
        // Sem a lista, o campo fica só com a opção vazia: o cadastro continua.
    }
    const padrao = opcoes.find(o => o.padrao);
    const rotulo = typeof rotuloSemModelo === 'function' ? rotuloSemModelo(padrao) : rotuloSemModelo;
    select.innerHTML = `<option value="">${escaparHtml(rotulo)}</option>` +
        opcoes.filter(o => !o.padrao || o.id === valorAtual)
            .map(o => `<option value="${o.id}">${escaparHtml(o.nome)}</option>`).join('');
    select.value = valorAtual ?? '';
    select.dataset.original = valorAtual ?? '';
}

/** Grava o modelo escolhido, só se mudou. url: /api/empresas/{id}/modelo-email-nota ou /api/clientes/{id}/modelo-email-nota. */
async function salvarModeloEmailSeMudou(select, url) {
    if (!select || select.value === (select.dataset.original ?? '')) return;
    await apiFetch(url, { method: 'PUT', body: JSON.stringify({ modeloId: select.value || null }) });
    select.dataset.original = select.value;
}

/**
 * UX2: no celular as listas viram cartões (site.css). Cada célula ganha
 * data-label com o título da coluna (o CSS mostra "rótulo: valor"). Vale
 * para as tabelas do DataTables e para as marcadas com .tabela-cartoes,
 * inclusive as que são redesenhadas depois (paginação, filtros, modais).
 */
function rotularCelulasTabela(tabela) {
    const titulos = [...tabela.querySelectorAll(':scope > thead > tr:last-child > th')].map(th => th.textContent.replace(/\s+/g, ' ').trim());
    if (titulos.length === 0) return;
    tabela.querySelectorAll(':scope > tbody > tr').forEach(tr => {
        let coluna = 0;
        [...tr.children].forEach(td => {
            if (!td.hasAttribute('data-label')) td.setAttribute('data-label', titulos[coluna] ?? '');
            coluna += td.colSpan || 1;
        });
    });
}

(function () {
    let agendado = false;
    function rotularTudo() {
        agendado = false;
        document.querySelectorAll('table.dataTable, table.tabela-cartoes').forEach(rotularCelulasTabela);
    }
    function agendar() {
        if (agendado) return;
        agendado = true;
        requestAnimationFrame(rotularTudo);
    }
    document.addEventListener('DOMContentLoaded', function () {
        rotularTudo();
        new MutationObserver(agendar).observe(document.body, { childList: true, subtree: true });
    });
})();
