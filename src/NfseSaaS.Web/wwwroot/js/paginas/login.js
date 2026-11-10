const formLogin = document.getElementById('form-login');
const formCodigo = document.getElementById('form-codigo');
const erroDiv = document.getElementById('erro-login');

// Vem de data-return-url no <form> (ver Login.cshtml) — Razor já
// escapou como atributo HTML, então dataset devolve a string
// decodificada direto, sem precisar de Html.Raw+JsonSerializer.
const returnUrl = formLogin.dataset.returnUrl || '/';

function mostrarErroLogin(texto, tipo = 'danger') {
    erroDiv.className = `alert alert-${tipo}`;
    erroDiv.textContent = texto;
}

function esconderErroLogin() {
    erroDiv.className = 'alert alert-danger d-none';
}

async function postar(url, corpo) {
    return fetch(url, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
        body: corpo === undefined ? undefined : JSON.stringify(corpo)
    });
}

formLogin.addEventListener('submit', async function (e) {
    e.preventDefault();
    esconderErroLogin();

    try {
        const resposta = await postar('/api/auth/login', {
            email: document.getElementById('email').value,
            senha: document.getElementById('senha').value
        });

        if (!resposta.ok) {
            if (resposta.status === 423 || resposta.status === 403) {
                // 423: bloqueio por tentativas; 403: conta suspensa pela plataforma.
                const corpo = await resposta.json().catch(() => null);
                mostrarErroLogin(corpo?.erro ?? 'Não é possível entrar com esta conta agora.');
            } else {
                mostrarErroLogin('E-mail ou senha inválidos.');
            }
            return;
        }

        // Verificação em duas etapas (B3): senha certa, falta o código.
        const corpo = await resposta.json().catch(() => null);
        if (corpo?.requerCodigo) {
            document.getElementById('email-codigo').textContent = corpo.email;
            document.getElementById('minutos-codigo').textContent = corpo.minutos;
            document.getElementById('dias-lembrar').textContent = corpo.diasLembrar;
            formLogin.classList.add('d-none');
            formCodigo.classList.remove('d-none');
            document.getElementById('codigo').focus();
            if (!corpo.enviado)
                mostrarErroLogin('Não conseguimos enviar o código agora. Clique em "Enviar outro código" em instantes.', 'warning');
            return;
        }

        window.location.href = returnUrl;
    } catch (err) {
        mostrarErroLogin('Não foi possível entrar. Tente novamente.');
    }
});

formCodigo.addEventListener('submit', async function (e) {
    e.preventDefault();
    esconderErroLogin();

    const codigo = document.getElementById('codigo').value.replace(/\D/g, '');
    if (codigo.length !== 6) {
        mostrarErroLogin('O código tem 6 números.');
        return;
    }

    try {
        const resposta = await postar('/api/auth/login-codigo', {
            codigo,
            lembrar: document.getElementById('lembrar-navegador').checked
        });

        if (resposta.ok) {
            window.location.href = returnUrl;
            return;
        }

        const corpo = await resposta.json().catch(() => null);
        mostrarErroLogin(corpo?.erro ?? 'Código inválido.');
        // Sessão da etapa 2 expirou ou conta bloqueada: volta para a senha.
        if (resposta.status === 401 || resposta.status === 423) {
            formCodigo.classList.add('d-none');
            formLogin.classList.remove('d-none');
            document.getElementById('senha').value = '';
        }
    } catch (err) {
        mostrarErroLogin('Não foi possível confirmar o código. Tente novamente.');
    }
});

document.getElementById('link-reenviar-codigo').addEventListener('click', async function (e) {
    e.preventDefault();
    esconderErroLogin();
    try {
        const resposta = await postar('/api/auth/login-codigo/reenviar');
        const corpo = await resposta.json().catch(() => null);
        if (resposta.ok)
            mostrarErroLogin(`Enviamos outro código para ${corpo?.email ?? 'o seu e-mail'}.`, 'success');
        else if (resposta.status === 429)
            mostrarErroLogin('Muitos pedidos seguidos. Espere alguns minutos e tente de novo.');
        else
            mostrarErroLogin(corpo?.erro ?? 'Não foi possível enviar outro código.');
    } catch (err) {
        mostrarErroLogin('Não foi possível enviar outro código.');
    }
});
