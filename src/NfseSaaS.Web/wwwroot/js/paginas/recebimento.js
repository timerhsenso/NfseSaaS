// CR1: página pública /recebimento/confirmar (sem login).
(function () {
    const form = document.getElementById('form-recebimento');
    if (!form) return;

    function mostrar(id, texto) {
        const div = document.getElementById(id);
        div.textContent = texto;
        div.classList.remove('d-none');
    }

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        ['erro-recebimento', 'sucesso-recebimento'].forEach(id => document.getElementById(id).classList.add('d-none'));
        const campo = document.getElementById('email-recebimento');
        if (!campo.value.trim() || !campo.checkValidity()) {
            mostrar('erro-recebimento', 'Informe o e-mail em que você recebeu a nota.');
            campo.focus();
            return;
        }

        const botao = form.querySelector('button[type=submit]');
        await executarComBotaoDesabilitado(botao, async function () {
            try {
                const resposta = await fetch('/api/recebimento/confirmar', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
                    body: JSON.stringify({ token: document.getElementById('token-recebimento').value, email: campo.value.trim() })
                });
                const corpo = await resposta.json().catch(() => null);
                if (resposta.status === 429)
                    throw new Error('Muitas tentativas. Aguarde alguns minutos e tente de novo.');
                if (!resposta.ok)
                    throw new Error(extrairMensagemDeErro(corpo) || 'Não foi possível confirmar. Tente novamente.');
                mostrar('sucesso-recebimento', corpo?.mensagem ?? 'Recebimento confirmado. Obrigado!');
                form.classList.add('d-none');
            } catch (err) {
                mostrar('erro-recebimento', err.message);
            }
        });
    });
})();
