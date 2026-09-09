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
