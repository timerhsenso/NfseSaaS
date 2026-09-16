// window.usuarioAtual vem de data-usuario-atual no <body> (ver
// _Layout.cshtml) — só string simples, não precisa mais de
// Html.Raw+JsonSerializer pra escapar: atributo HTML já é escapado pelo
// próprio Razor, e dataset devolve a string decodificada direto.
window.usuarioAtual = document.body.dataset.usuarioAtual;

// Sidebar em telas pequenas — substitui o toggle que vinha do AdminLTE
// (removido junto com o resto da lib).
(function () {
    var sidebar = document.getElementById('sidebar');
    var overlay = document.getElementById('overlaySidebar');
    var botao = document.getElementById('botaoMenu');

    function fechar() {
        sidebar.classList.remove('open');
        overlay.classList.remove('show');
    }

    botao?.addEventListener('click', function () {
        sidebar.classList.toggle('open');
        overlay.classList.toggle('show');
    });
    overlay?.addEventListener('click', fechar);
})();

// Logout: chama a mesma API usada pelo Swagger/clients externos — a
// tela MVC nunca duplica a lógica de autenticação, só consome o
// endpoint /api/auth/* via fetch (mesmo padrão de todas as outras
// telas, que consomem /api/empresas, /api/clientes etc.).
document.getElementById('link-sair')?.addEventListener('click', async function (e) {
    e.preventDefault();
    await fetch('/api/auth/logout', { method: 'POST', headers: obterCsrfHeader() });
    window.location.href = '/Account/Login';
});

// Tema claro/escuro — preferência só deste navegador (localStorage, sem
// banco). tema-inicial.js já aplicou o tema salvo antes de renderizar
// (evita o "flash" de tela clara); aqui só cuida do clique.
(function () {
    var icone = document.getElementById('iconeTema');

    function atualizarIcone(tema) {
        icone.className = tema === 'dark' ? 'bi bi-sun' : 'bi bi-moon-stars';
    }

    atualizarIcone(document.documentElement.getAttribute('data-theme'));

    document.getElementById('botaoTema')?.addEventListener('click', function () {
        var temaAtual = document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
        var novoTema = temaAtual === 'dark' ? 'light' : 'dark';

        document.documentElement.setAttribute('data-theme', novoTema);
        localStorage.setItem('nfsesaas-tema', novoTema);
        atualizarIcone(novoTema);
    });
})();
