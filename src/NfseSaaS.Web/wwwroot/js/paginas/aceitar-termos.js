// Tela de aceite (/Legal/Aceitar) — ver ExigeAceiteTermosFilter.
// JUR1: Termos e Política (todos), DPA e autorizações (administrador, uma
// caixa por empresa). A emissão automática pede a senha antes de gravar.
(function () {
    const form = document.getElementById('form-aceite');
    const erroDiv = document.getElementById('erro-aceite');
    const caixaSenha = document.getElementById('caixa-senha-automatica');

    // Só volta para endereço do próprio site (o servidor já validou; aqui
    // é a segunda barreira contra redirecionamento aberto).
    function destinoSeguro() {
        const destino = form.dataset.returnUrl || '/';
        return destino.startsWith('/') && !destino.startsWith('//') && !destino.startsWith('/\\') ? destino : '/';
    }

    function mostrarErro(mensagem) {
        erroDiv.textContent = mensagem;
        erroDiv.classList.remove('d-none');
        erroDiv.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function marcados(nome) {
        return Array.from(form.querySelectorAll(`input[name="${nome}"]:checked`)).map(c => c.value);
    }

    form.querySelectorAll('input[name="automatica"]').forEach(caixa => caixa.addEventListener('change', () => {
        caixaSenha?.classList.toggle('d-none', marcados('automatica').length === 0);
    }));

    async function postar(url, corpo) {
        const resposta = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify(corpo)
        });
        if (resposta.status === 401) {
            window.location.href = '/Account/Login';
            return null;
        }
        if (!resposta.ok) {
            const conteudo = await resposta.json().catch(() => null);
            throw new Error(extrairMensagemDeErro(conteudo) ?? 'Não foi possível registrar o aceite.');
        }
        return resposta;
    }

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        erroDiv.classList.add('d-none');

        const termos = form.dataset.termos === '1';
        if (termos && !document.getElementById('aceito').checked) {
            mostrarErro('Marque a caixa confirmando que leu e aceita os Termos de uso e a Política de privacidade.');
            return;
        }
        const caixaDpa = document.getElementById('aceito-dpa');
        if (form.dataset.dpaExigido === '1' && caixaDpa && !caixaDpa.checked) {
            mostrarErro('Marque a caixa do Acordo de Tratamento de Dados: como administrador, você aceita em nome da conta.');
            return;
        }

        const automaticas = marcados('automatica');
        const senha = document.getElementById('senha-automatica')?.value ?? '';
        if (automaticas.length > 0 && !senha) {
            mostrarErro('Confirme a sua senha para autorizar a emissão automática.');
            document.getElementById('senha-automatica')?.focus();
            return;
        }

        const botao = document.getElementById('btn-aceitar');
        botao.disabled = true;
        try {
            if (automaticas.length > 0 && !(await postar('/api/auth/confirmar-senha', { senha })))
                return;

            const ok = await postar('/api/legal/aceitar', {
                aceito: termos,
                aceitoDpa: caixaDpa?.checked ?? false,
                certificados: marcados('certificado'),
                emissaoAutomatica: automaticas
            });
            if (ok)
                window.location.href = destinoSeguro();
        } catch (err) {
            mostrarErro(err.message || 'Não foi possível registrar o aceite. Tente novamente.');
            botao.disabled = false;
        }
    });

    document.getElementById('link-sair-aceite')?.addEventListener('click', async function (e) {
        e.preventDefault();
        await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin', headers: obterCsrfHeader() }).catch(() => null);
        window.location.href = '/Account/Login';
    });
})();
