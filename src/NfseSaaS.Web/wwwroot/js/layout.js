// window.usuarioAtual vem de data-usuario-atual no <body> (ver
// _Layout.cshtml) — só string simples, não precisa mais de
// Html.Raw+JsonSerializer pra escapar: atributo HTML já é escapado pelo
// próprio Razor, e dataset devolve a string decodificada direto.
window.usuarioAtual = document.body.dataset.usuarioAtual;

// Botão do menu (☰), sempre visível:
// - computador (> 992px): recolhe/expande o menu lateral para só ícones;
//   a escolha fica salva neste navegador (tema-inicial.js aplica cedo);
// - celular/tablet (≤ 992px): abre/fecha o menu por cima da tela.
(function () {
    var sidebar = document.getElementById('sidebar');
    var overlay = document.getElementById('overlaySidebar');
    var botao = document.getElementById('botaoMenu');
    var raiz = document.documentElement;
    var telaPequena = window.matchMedia('(max-width: 992px)');

    // Com o menu recolhido, o nome de cada item vira dica (tooltip nativo).
    sidebar?.querySelectorAll('.sidebar-nav a').forEach(function (a) {
        if (!a.title) a.title = a.textContent.trim();
    });

    function fecharMobile() {
        sidebar.classList.remove('open');
        overlay.classList.remove('show');
        botao?.setAttribute('aria-expanded', 'false');
    }

    function atualizarRotulo() {
        if (!botao) return;
        var aberto = telaPequena.matches ? sidebar.classList.contains('open') : !raiz.classList.contains('menu-recolhido');
        botao.setAttribute('aria-expanded', aberto ? 'true' : 'false');
        botao.setAttribute('aria-label', aberto ? 'Recolher menu' : 'Mostrar menu');
        botao.title = aberto ? 'Recolher menu' : 'Mostrar menu';
    }

    botao?.addEventListener('click', function () {
        if (telaPequena.matches) {
            sidebar.classList.toggle('open');
            overlay.classList.toggle('show');
        } else {
            var recolher = !raiz.classList.contains('menu-recolhido');
            raiz.classList.toggle('menu-recolhido', recolher);
            try { localStorage.setItem('micoempresa-menu', recolher ? 'recolhido' : 'aberto'); } catch (e) { }
        }
        atualizarRotulo();
    });

    overlay?.addEventListener('click', function () { fecharMobile(); atualizarRotulo(); });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && sidebar.classList.contains('open')) { fecharMobile(); atualizarRotulo(); }
    });
    // Voltou para tela grande com o menu móvel aberto: fecha o painel.
    telaPequena.addEventListener('change', function () { fecharMobile(); atualizarRotulo(); });

    atualizarRotulo();
})();

// Logout: chama a mesma API usada pelo Swagger/clients externos — a
// tela MVC nunca duplica a lógica de autenticação, só consome o
// endpoint /api/auth/* via fetch (mesmo padrão de todas as outras
// telas, que consomem /api/empresas, /api/clientes etc.).
document.getElementById('link-sair')?.addEventListener('click', async function (e) {
    e.preventDefault();
    await fetch('/api/auth/logout', { method: 'POST', headers: obterCsrfHeader() });
    // A próxima conta que entrar neste navegador não herda a Empresa desta.
    if (typeof limparEmpresaAtualId === 'function') limparEmpresaAtualId();
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
