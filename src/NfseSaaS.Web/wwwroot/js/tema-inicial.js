// Aplica o tema salvo ANTES do resto da página renderizar — sem isso, a
// tela pisca clara por uma fração de segundo antes de escurecer (o CSS
// só carrega/aplica depois). localStorage, não banco — a preferência é
// só deste navegador, como pedido.
//
// Carregado no <head>, SEM defer/async, de propósito — precisa bloquear
// o parsing até rodar, senão o efeito de evitar o flash se perde (é
// exatamente o mesmo motivo pelo qual isto era inline antes).
(function () {
    var tema = localStorage.getItem('nfsesaas-tema') || 'light';
    document.documentElement.setAttribute('data-theme', tema);
})();
