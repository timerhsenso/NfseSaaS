document.getElementById('form-login').addEventListener('submit', async function (e) {
    e.preventDefault();
    const erroDiv = document.getElementById('erro-login');
    erroDiv.classList.add('d-none');

    // Vem de data-return-url no <form> (ver Login.cshtml) — Razor já
    // escapou como atributo HTML, então dataset devolve a string
    // decodificada direto, sem precisar de Html.Raw+JsonSerializer.
    const returnUrl = e.target.dataset.returnUrl || '/';

    try {
        const resposta = await fetch('/api/auth/login', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({
                email: document.getElementById('email').value,
                senha: document.getElementById('senha').value
            })
        });

        if (!resposta.ok) {
            if (resposta.status === 423 || resposta.status === 403) {
                // 423: bloqueio por tentativas; 403: conta suspensa pela plataforma.
                const corpo = await resposta.json().catch(() => null);
                erroDiv.textContent = corpo?.erro ?? 'Não é possível entrar com esta conta agora.';
            } else {
                erroDiv.textContent = 'E-mail ou senha inválidos.';
            }
            erroDiv.classList.remove('d-none');
            return;
        }

        window.location.href = returnUrl;
    } catch (err) {
        erroDiv.textContent = 'Não foi possível entrar. Tente novamente.';
        erroDiv.classList.remove('d-none');
    }
});
