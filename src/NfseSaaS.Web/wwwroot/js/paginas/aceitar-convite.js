document.getElementById('form-convite').addEventListener('submit', async function (e) {
    e.preventDefault();
    const erroDiv = document.getElementById('erro-convite');
    erroDiv.classList.add('d-none');

    try {
        const resposta = await fetch('/api/auth/aceitar-convite', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({
                email: document.getElementById('email').value,
                token: document.getElementById('token').value,
                novaSenha: document.getElementById('novaSenha').value
            })
        });

        if (!resposta.ok) {
            const corpo = await resposta.json().catch(() => null);
            erroDiv.textContent = extrairMensagemDeErro(corpo) ?? 'Não foi possível aceitar o convite.';
            erroDiv.classList.remove('d-none');
            return;
        }

        window.location.href = '/';
    } catch (err) {
        erroDiv.textContent = 'Não foi possível aceitar o convite. Tente novamente.';
        erroDiv.classList.remove('d-none');
    }
});
