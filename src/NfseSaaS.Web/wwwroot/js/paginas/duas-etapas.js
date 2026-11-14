// Tela obrigatória "Ligue a verificação em duas etapas" (/Seguranca/DuasEtapas)
// — ver ExigeDuploFatorFilter (SEG1). Mesma API do menu do usuário.
(function () {
    const area = document.getElementById('duas-etapas');
    const erroDiv = document.getElementById('erro-duas-etapas');
    const sucessoDiv = document.getElementById('sucesso-duas-etapas');
    const form = document.getElementById('form-duas-etapas');
    const botaoEnviar = document.getElementById('btn-duas-etapas-enviar');

    // Só volta para endereço do próprio site (o servidor já validou).
    function destinoSeguro() {
        const destino = area.dataset.returnUrl || '/';
        return destino.startsWith('/') && !destino.startsWith('//') && !destino.startsWith('/\\') ? destino : '/';
    }

    function mensagem(div, texto) {
        erroDiv.classList.add('d-none');
        sucessoDiv.classList.add('d-none');
        if (!div) return;
        div.textContent = texto;
        div.classList.remove('d-none');
    }

    async function enviarCodigo(botao) {
        mensagem(null);
        await executarComBotaoDesabilitado(botao, async function () {
            try {
                const r = await apiFetch('/api/auth/duplo-fator/codigo', { method: 'POST' });
                mensagem(sucessoDiv, `Enviamos um código para ${r.email}. Digite-o abaixo.`);
                botaoEnviar.classList.add('d-none');
                form.classList.remove('d-none');
                document.getElementById('codigo-duas-etapas').focus();
            } catch (err) {
                mensagem(erroDiv, err.message);
            }
        });
    }

    botaoEnviar.addEventListener('click', function () { enviarCodigo(botaoEnviar); });
    document.getElementById('btn-duas-etapas-reenviar').addEventListener('click', function (e) { enviarCodigo(e.currentTarget); });

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        const codigo = document.getElementById('codigo-duas-etapas').value.replace(/\D/g, '');
        if (codigo.length !== 6) {
            mensagem(erroDiv, 'O código tem 6 números.');
            return;
        }
        const botao = form.querySelector('button[type="submit"]');
        await executarComBotaoDesabilitado(botao, async function () {
            try {
                await apiFetch('/api/auth/duplo-fator/ativar', { method: 'POST', body: JSON.stringify({ codigo }) });
                mensagem(sucessoDiv, 'Pronto! Verificação em duas etapas ligada.');
                window.location.href = destinoSeguro();
            } catch (err) {
                mensagem(erroDiv, err.message);
            }
        });
    });

    document.getElementById('link-sair-duas-etapas').addEventListener('click', async function (e) {
        e.preventDefault();
        await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin', headers: obterCsrfHeader() }).catch(() => null);
        window.location.href = '/Account/Login';
    });
})();
