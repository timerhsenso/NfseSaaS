// Painel "Aparência" (offcanvas do topo) e preferências de aparência.
//
// - O estado SALVO vem do <html> (o servidor já renderiza data-paleta,
//   data-fonte, data-densidade, data-modo e a classe menu-recolhido).
// - Cada clique no painel aplica na hora (pré-visualização). "Salvar"
//   grava; fechar sem salvar volta ao estado salvo.
// - Cores e logo da conta NÃO ficam aqui: são da conta, não da pessoa —
//   vão em "Configurações da conta" (menu do usuário, só administrador;
//   paginas/configuracoes-conta.js). Aqui a paleta salva só é mantida.
// - Atalhos do topo (sol/lua e o ☰ do menu) gravam pela mesma função,
//   window.salvarPreferenciasAparencia.
// - Quem nunca salvou (data-aparencia-salva="0"): grava uma vez o que este
//   navegador tinha guardado (tema e menu), para valer em qualquer lugar.
(function () {
    var raiz = document.documentElement;
    var escuroDoSistema = window.matchMedia('(prefers-color-scheme: dark)');

    function lerDoHtml() {
        return {
            paleta: raiz.dataset.paleta || 'mico',
            fonte: raiz.dataset.fonte || 'm',
            densidade: raiz.dataset.densidade || 'padrao',
            modo: raiz.dataset.modo || (raiz.getAttribute('data-theme') === 'dark' ? 'escuro' : 'claro'),
            menuRecolhido: raiz.classList.contains('menu-recolhido')
        };
    }

    function temaDoModo(modo) {
        return modo === 'escuro' || (modo === 'auto' && escuroDoSistema.matches) ? 'dark' : 'light';
    }

    function aplicar(estado) {
        raiz.dataset.paleta = estado.paleta;
        raiz.dataset.fonte = estado.fonte;
        raiz.dataset.densidade = estado.densidade;
        raiz.dataset.modo = estado.modo;
        raiz.setAttribute('data-theme', temaDoModo(estado.modo));
        if (!window.matchMedia('(max-width: 992px)').matches) {
            raiz.classList.toggle('menu-recolhido', !!estado.menuRecolhido);
        }
        document.dispatchEvent(new CustomEvent('aparencia:aplicada'));
    }

    // Mantém as telas sem login (entrada, cadastro) com o mesmo tema.
    function guardarNoNavegador(estado) {
        try {
            localStorage.setItem('nfsesaas-tema', temaDoModo(estado.modo));
            localStorage.setItem('micoempresa-menu', estado.menuRecolhido ? 'recolhido' : 'aberto');
        } catch (e) { /* sem storage */ }
    }

    function soPreferencias(estado) {
        return { fonte: estado.fonte, densidade: estado.densidade, modo: estado.modo, menuRecolhido: !!estado.menuRecolhido };
    }

    var salvo = lerDoHtml();

    // Atalhos do topo e migração: grava só as preferências do usuário.
    window.salvarPreferenciasAparencia = async function (parcial) {
        salvo = Object.assign({}, salvo, parcial || {});
        guardarNoNavegador(salvo);
        try {
            await apiFetch('/api/aparencia/preferencias', { method: 'PUT', body: JSON.stringify(soPreferencias(salvo)) });
            raiz.dataset.aparenciaSalva = '1';
        } catch (e) {
            // Atalho não deve incomodar: fica valendo neste navegador e tenta de novo no próximo clique.
        }
    };

    if (raiz.dataset.aparenciaSalva === '0') {
        raiz.dataset.modo = salvo.modo;
        window.salvarPreferenciasAparencia({});
    }

    // Modo automático acompanha o sistema operacional sem recarregar.
    escuroDoSistema.addEventListener('change', function () {
        if (raiz.dataset.modo === 'auto') aplicar(lerDoHtml());
    });

    var painel = document.getElementById('painelAparencia');
    if (!painel) return;

    var atual = Object.assign({}, salvo);
    var salvando = false;

    function marcar() {
        painel.querySelectorAll('.aparencia-opcoes').forEach(function (grupo) {
            var campo = grupo.dataset.campo;
            grupo.querySelectorAll('button').forEach(function (b) {
                var sel = b.dataset.valor === atual[campo];
                b.classList.toggle('active', sel);
                b.setAttribute('aria-checked', sel ? 'true' : 'false');
            });
        });
        document.getElementById('aparencia-menu-recolhido').checked = !!atual.menuRecolhido;
    }

    function mudar(parcial) {
        atual = Object.assign({}, atual, parcial);
        aplicar(atual);
        marcar();
    }

    painel.addEventListener('click', function (e) {
        var opcao = e.target.closest('.aparencia-opcoes button');
        if (opcao) {
            var parcial = {};
            parcial[opcao.closest('.aparencia-opcoes').dataset.campo] = opcao.dataset.valor;
            mudar(parcial);
        }
    });

    document.getElementById('aparencia-menu-recolhido').addEventListener('change', function () {
        mudar({ menuRecolhido: this.checked });
    });

    document.getElementById('aparencia-restaurar').addEventListener('click', function () {
        mudar({
            paleta: atual.paleta,
            fonte: 'm', densidade: 'padrao', modo: 'claro', menuRecolhido: false
        });
    });

    document.getElementById('aparencia-salvar').addEventListener('click', async function () {
        var botao = this;
        await executarComBotaoDesabilitado(botao, async function () {
            try {
                await apiFetch('/api/aparencia/preferencias', { method: 'PUT', body: JSON.stringify(soPreferencias(atual)) });
                salvo = Object.assign({}, atual);
                raiz.dataset.aparenciaSalva = '1';
                guardarNoNavegador(salvo);
                salvando = true;
                bootstrap.Offcanvas.getOrCreateInstance(painel).hide();
                mostrarToast('Aparência salva.');
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });

    painel.addEventListener('show.bs.offcanvas', function () {
        salvando = false;
        atual = Object.assign({}, salvo);
        marcar();
    });

    // Fechou sem salvar: desfaz a pré-visualização.
    painel.addEventListener('hidden.bs.offcanvas', function () {
        if (!salvando) {
            atual = Object.assign({}, salvo);
            aplicar(salvo);
        }
        salvando = false;
    });
})();
