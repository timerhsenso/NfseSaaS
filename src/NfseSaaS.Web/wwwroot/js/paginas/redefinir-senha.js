document.getElementById('form-redefinir-senha').addEventListener('submit', async function (e) {
    e.preventDefault();
    const erroDiv = document.getElementById('erro-redefinir-senha');
    erroDiv.classList.add('d-none');

    const novaSenha = document.getElementById('novaSenha').value;
    const confirmarNovaSenha = document.getElementById('confirmarNovaSenha').value;

    if (novaSenha !== confirmarNovaSenha) {
        erroDiv.textContent = 'A confirmação não bate com a nova senha.';
        erroDiv.classList.remove('d-none');
        return;
    }

    try {
        const resposta = await fetch('/api/auth/redefinir-senha', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({
                email: document.getElementById('email').value,
                token: document.getElementById('token').value,
                novaSenha
            })
        });

        if (!resposta.ok) {
            const corpo = await resposta.json().catch(() => null);
            erroDiv.textContent = extrairMensagemDeErro(corpo) ?? 'Não foi possível redefinir a senha.';
            erroDiv.classList.remove('d-none');
            return;
        }

        window.location.href = '/';
    } catch (err) {
        erroDiv.textContent = 'Não foi possível redefinir a senha. Tente novamente.';
        erroDiv.classList.remove('d-none');
    }
});
