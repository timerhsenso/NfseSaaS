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
    const duracaoMs = ehErro ? 5000 : 3000;

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
    toast.querySelector('.toast-nfse-texto').textContent = mensagem;

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

function mostrarErroFormulario(idDiv, mensagem) {
    const div = document.getElementById(idDiv);
    if (!div) return;
    div.textContent = mensagem;
    div.classList.remove('d-none');
}

function ocultarErroFormulario(idDiv) {
    const div = document.getElementById(idDiv);
    if (!div) return;
    div.classList.add('d-none');
}

function mostrarResultado(idDiv, sucesso, mensagem) {
    const div = document.getElementById(idDiv);
    if (!div) return;
    div.className = `alert ${sucesso ? 'alert-success' : 'alert-danger'} mt-3`;
    div.textContent = mensagem;
    div.classList.remove('d-none');
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
