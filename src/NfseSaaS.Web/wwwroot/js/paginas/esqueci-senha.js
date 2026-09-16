document.getElementById('form-esqueci-senha').addEventListener('submit', async function (e) {
    e.preventDefault();
    const erroDiv = document.getElementById('erro-esqueci-senha');
    const sucessoDiv = document.getElementById('sucesso-esqueci-senha');
    erroDiv.classList.add('d-none');
    sucessoDiv.classList.add('d-none');

    try {
        const resposta = await fetch('/api/auth/esqueci-senha', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({ email: document.getElementById('email').value })
        });

        const corpo = await resposta.json().catch(() => null);

        sucessoDiv.textContent = corpo?.mensagem ?? 'Se esse e-mail existir na nossa base, enviamos um link para redefinir a senha.';
        sucessoDiv.classList.remove('d-none');
        document.getElementById('form-esqueci-senha').reset();
    } catch (err) {
        erroDiv.textContent = 'Não foi possível enviar o pedido. Tente novamente.';
        erroDiv.classList.remove('d-none');
    }
});
