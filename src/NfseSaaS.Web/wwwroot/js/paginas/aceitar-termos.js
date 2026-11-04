// Tela de aceite dos Termos de uso e da Política de privacidade
// (/Legal/Aceitar) — ver ExigeAceiteTermosFilter.
(function () {
    const form = document.getElementById('form-aceite');
    const erroDiv = document.getElementById('erro-aceite');

    // Só volta para endereço do próprio site (o servidor já validou; aqui
    // é a segunda barreira contra redirecionamento aberto).
    function destinoSeguro() {
        const destino = form.dataset.returnUrl || '/';
        return destino.startsWith('/') && !destino.startsWith('//') && !destino.startsWith('/\\') ? destino : '/';
    }

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        erroDiv.classList.add('d-none');

        if (!document.getElementById('aceito').checked) {
            erroDiv.textContent = 'Marque a caixa confirmando que leu e aceita os documentos.';
            erroDiv.classList.remove('d-none');
            return;
        }

        const botao = form.querySelector('button[type="submit"]');
        botao.disabled = true;

        try {
            const resposta = await fetch('/api/legal/aceitar', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
                body: JSON.stringify({ aceito: true })
            });

            if (resposta.status === 401) {
                window.location.href = '/Account/Login';
                return;
            }

            if (!resposta.ok) {
                const corpo = await resposta.json().catch(() => null);
                erroDiv.textContent = extrairMensagemDeErro(corpo) ?? 'Não foi possível registrar o aceite.';
                erroDiv.classList.remove('d-none');
                botao.disabled = false;
                return;
            }

            window.location.href = destinoSeguro();
        } catch (err) {
            erroDiv.textContent = 'Não foi possível registrar o aceite. Tente novamente.';
            erroDiv.classList.remove('d-none');
            botao.disabled = false;
        }
    });

    document.getElementById('link-sair-aceite').addEventListener('click', async function (e) {
        e.preventDefault();
        await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin', headers: obterCsrfHeader() }).catch(() => null);
        window.location.href = '/Account/Login';
    });
})();
