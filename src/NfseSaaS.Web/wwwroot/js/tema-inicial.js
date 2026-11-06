// Aplica tema (claro/escuro) e menu recolhido ANTES do resto da página
// renderizar — sem isso, a tela pisca clara por uma fração de segundo
// antes de escurecer. Carregado no <head>, SEM defer/async, de propósito.
//
// Logado com aparência salva: o servidor já mandou no <html> o data-modo
// (claro, escuro ou auto) e a classe do menu; aqui só resolvemos o "auto"
// pelo sistema operacional. Sem login (telas de entrada) ou quem ainda não
// salvou: vale o que este navegador guardou (localStorage), como antes.
(function () {
    var raiz = document.documentElement;
    var modo = raiz.getAttribute('data-modo');
    var tema = 'light';

    if (modo) {
        tema = modo === 'escuro' || (modo === 'auto' && window.matchMedia('(prefers-color-scheme: dark)').matches)
            ? 'dark' : 'light';
    } else {
        try {
            tema = localStorage.getItem('nfsesaas-tema') || 'light';
            if (localStorage.getItem('micoempresa-menu') === 'recolhido') raiz.classList.add('menu-recolhido');
        } catch (e) { /* navegação privada sem storage: usa o padrão */ }
    }

    raiz.setAttribute('data-theme', tema);
})();
