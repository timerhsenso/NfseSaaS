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
        headers: { 'Content-Type': 'application/json' },
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
 * 'sucesso' (padrão) ou 'erro' (fundo vermelho).
 */
function mostrarToast(mensagem, tipo) {
    const toast = document.getElementById('toastNfse');
    if (!toast) {
        // Fallback pras páginas que ainda não usam o _Layout novo.
        alert(mensagem);
        return;
    }

    toast.textContent = mensagem;
    toast.classList.toggle('toast-erro', tipo === 'erro');
    toast.classList.add('show');

    clearTimeout(window.__toastTimer);
    window.__toastTimer = setTimeout(() => toast.classList.remove('show'), tipo === 'erro' ? 4000 : 2500);
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
