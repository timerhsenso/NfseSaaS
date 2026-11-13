// RC1: páginas públicas /copia/sair e /copia/confirmar (sem login).
(function () {
    function mostrar(id, texto) {
        const div = document.getElementById(id);
        if (!div) return;
        div.textContent = texto;
        div.classList.remove('d-none');
    }

    function esconder(...ids) {
        ids.forEach(id => document.getElementById(id)?.classList.add('d-none'));
    }

    async function enviar(url, dados) {
        const resposta = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify(dados)
        });
        const corpo = await resposta.json().catch(() => null);
        if (resposta.status === 429)
            throw new Error('Muitas tentativas. Aguarde alguns minutos e tente de novo.');
        if (!resposta.ok)
            throw new Error(extrairMensagemDeErro(corpo) || 'Não foi possível concluir. Tente novamente.');
        return corpo;
    }

    const formPedido = document.getElementById('form-saida-copia');
    formPedido?.addEventListener('submit', async function (e) {
        e.preventDefault();
        esconder('erro-saida-copia', 'sucesso-saida-copia', 'aviso-saida-copia');
        const campo = document.getElementById('email-saida-copia');
        if (!campo.value.trim() || !campo.checkValidity()) {
            mostrar('erro-saida-copia', 'Informe um e-mail válido.');
            campo.focus();
            return;
        }
        const botao = formPedido.querySelector('button[type=submit]');
        await executarComBotaoDesabilitado(botao, async function () {
            try {
                const corpo = await enviar('/api/copia/pedir', {
                    token: document.getElementById('token-saida-copia').value,
                    email: campo.value.trim()
                });
                if (corpo?.principal) {
                    mostrar('aviso-saida-copia', corpo.mensagem);
                } else {
                    mostrar('sucesso-saida-copia', corpo?.mensagem ?? 'Enviamos o link de confirmação.');
                    formPedido.classList.add('d-none');
                }
            } catch (err) {
                mostrar('erro-saida-copia', err.message);
            }
        });
    });

    const formConfirmar = document.getElementById('form-confirmar-saida');
    formConfirmar?.addEventListener('submit', async function (e) {
        e.preventDefault();
        esconder('erro-confirmar-saida', 'sucesso-confirmar-saida');
        const botao = formConfirmar.querySelector('button[type=submit]');
        await executarComBotaoDesabilitado(botao, async function () {
            try {
                const corpo = await enviar('/api/copia/confirmar', {
                    codigo: document.getElementById('codigo-confirmar-saida').value
                });
                mostrar('sucesso-confirmar-saida', corpo?.mensagem ?? 'Pronto: você não recebe mais as cópias.');
                formConfirmar.classList.add('d-none');
            } catch (err) {
                mostrar('erro-confirmar-saida', err.message);
            }
        });
    });
})();
