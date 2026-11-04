document.getElementById('form-registrar').addEventListener('submit', async function (e) {
    e.preventDefault();
    const erroDiv = document.getElementById('erro-registrar');
    erroDiv.classList.add('d-none');

    const aceitouTermos = document.getElementById('aceitouTermos').checked;
    if (!aceitouTermos) {
        erroDiv.textContent = 'Para criar a conta, marque que leu e aceita os Termos de uso e a Política de privacidade.';
        erroDiv.classList.remove('d-none');
        return;
    }

    try {
        const resposta = await fetch('/api/auth/registrar', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({
                razaoSocialTenant: document.getElementById('razaoSocialTenant').value,
                cnpjTenant: document.getElementById('cnpjTenant').value,
                email: document.getElementById('email').value,
                senha: document.getElementById('senha').value,
                aceitouTermos: aceitouTermos
            })
        });

        if (!resposta.ok) {
            const corpo = await resposta.json().catch(() => null);
            erroDiv.textContent = extrairMensagemDeErro(corpo) ?? 'Não foi possível criar a conta.';
            erroDiv.classList.remove('d-none');
            return;
        }

        window.location.href = '/';
    } catch (err) {
        erroDiv.textContent = 'Não foi possível criar a conta. Tente novamente.';
        erroDiv.classList.remove('d-none');
    }
});
