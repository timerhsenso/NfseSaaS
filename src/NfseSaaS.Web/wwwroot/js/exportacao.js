/**
 * Padrão das telas de lista (11/11/2026 — telas mais limpas):
 *
 * - Ações da tela ficam no cabeçalho da página (seção "AcoesPagina" do
 *   _Layout): Exportar ▾, ⋯ (ações raras) e a ação principal.
 * - Logo acima da tabela, UMA barra (div.barra-lista, id "barra-<tabela>"):
 *   pesquisa + filtros da tela. Se a view não tiver a barra, ela é criada
 *   sozinha — toda tabela ganha a mesma pesquisa no mesmo lugar.
 * - Embaixo da tabela: "Exibir N", "Mostrando…" e a paginação.
 *
 * configurarExportacao(tabela, 'Empresas', { extras }) monta o menu
 * "Exportar" (Excel, CSV, Copiar, Imprimir + extras da tela, ex.: XML do
 * mês) com 2 exigências de sempre:
 *
 * 1. Toda exportação/impressão sai com um rodapé mostrando quem
 *    exportou e quando (data/hora do NAVEGADOR do usuário, no momento
 *    do clique — não precisa bater no servidor pra isso).
 * 2. Toda exportação/impressão é registrada no AuditLog do backend
 *    (POST /api/exportacoes) — é só o METADADO (tela + formato), nunca
 *    o conteúdo da grid; ver ExportacoesController.
 */
(function () {
    if (!window.DataTable) return;

    // Pesquisa sai do topo (vai para a barra da lista); rodapé com
    // "Exibir N" + "Mostrando…" à esquerda e a paginação à direita.
    // Larguras pelo conteúdo (CSS col-curta/col-codigo/col-valor), não
    // divididas por igual: código, valor, data e status ficam justos e a
    // sobra vai para os textos (descrição, nome, cliente).
    DataTable.defaults.autoWidth = false;

    DataTable.defaults.layout = {
        topStart: null,
        topEnd: null,
        bottomStart: ['pageLength', 'info'],
        bottomEnd: 'paging'
    };

    // Toda tabela, de qualquer tela, ganha a pesquisa na barra.
    $(document).on('init.dt', function (e, settings) {
        if (e.namespace !== 'dt') return;
        montarPesquisaDaLista(new DataTable.Api(settings));
    });
})();

/** Barra acima da tabela (a da view, ou uma nova logo antes da tabela). */
function barraDaLista(tabela) {
    const id = tabela.table().node().id;
    let barra = document.getElementById(`barra-${id}`);
    if (!barra) {
        barra = document.createElement('div');
        barra.id = `barra-${id}`;
        barra.className = 'barra-lista';
        tabela.table().container().before(barra);
    }
    return barra;
}

function montarPesquisaDaLista(tabela) {
    const barra = barraDaLista(tabela);
    // A view já tem a sua pesquisa (ex.: busca no servidor em E-mails).
    if (barra.querySelector('.busca-lista') || barra.dataset.semPesquisa === 'true') return;

    const caixa = document.createElement('label');
    caixa.className = 'busca-lista';
    caixa.innerHTML = '<i class="bi bi-search" aria-hidden="true"></i><input type="search" class="form-control" placeholder="Pesquisar…" aria-label="Pesquisar na lista" />';
    const campo = caixa.querySelector('input');
    campo.value = tabela.search() ?? '';

    let espera;
    campo.addEventListener('input', () => {
        clearTimeout(espera);
        espera = setTimeout(() => tabela.search(campo.value).draw(), 200);
    });

    barra.prepend(caixa);
}

/**
 * Menu "Exportar ▾". extras: [{ texto, icone, acao }] — itens da tela
 * que também levam dados para fora (aparecem depois de um divisor).
 * O menu vai para o elemento "botoes-exportacao-<tabela>" (cabeçalho da
 * página, por convenção); sem ele, vai para o fim da barra da lista.
 */
function configurarExportacao(tabela, nomeTela, opcoes = {}) {
    const linhaRodape = () => `Exportado por ${window.usuarioAtual ?? 'desconhecido'} em ${new Date().toLocaleString('pt-BR')}`;
    const nomeArquivo = () => `${nomeTela}_${dataLocalIso()}`;

    // Botões do DataTables (não aparecem na tela): o menu chama trigger(),
    // então o rodapé e o evento de auditoria continuam os mesmos.
    new $.fn.dataTable.Buttons(tabela, {
        buttons: [
            {
                extend: 'excelHtml5',
                filename: nomeArquivo,
                customizeData: function (data) {
                    data.body.push([linhaRodape()]);
                }
            },
            {
                extend: 'csvHtml5',
                filename: nomeArquivo,
                customizeData: function (data) {
                    data.body.push([linhaRodape()]);
                }
            },
            { extend: 'copyHtml5' },
            {
                extend: 'print',
                title: nomeTela,
                customize: function (win) {
                    $(win.document.body).append(`<div class="rodape-exportacao">${linhaRodape()}</div>`);
                }
            }
        ]
    });

    const itensPadrao = [
        { texto: 'Excel', icone: 'bi-file-earmark-excel', indice: 0 },
        { texto: 'CSV', icone: 'bi-filetype-csv', indice: 1 },
        { texto: 'Copiar', icone: 'bi-clipboard', indice: 2 },
        { texto: 'Imprimir', icone: 'bi-printer', indice: 3 }
    ];
    const extras = opcoes.extras ?? [];

    const menu = document.createElement('div');
    menu.className = 'dropdown';
    menu.innerHTML = `
        <button type="button" class="btn btn-outline-secondary btn-sm dropdown-toggle" data-bs-toggle="dropdown" aria-expanded="false">
            <i class="bi bi-download" aria-hidden="true"></i> Exportar
        </button>
        <ul class="dropdown-menu dropdown-menu-end">
            ${itensPadrao.map(i => `<li><button type="button" class="dropdown-item" data-exportar="${i.indice}"><i class="bi ${i.icone} me-2" aria-hidden="true"></i>${i.texto}</button></li>`).join('')}
            ${extras.length ? '<li><hr class="dropdown-divider"></li>' : ''}
            ${extras.map((x, n) => `<li><button type="button" class="dropdown-item" data-exportar-extra="${n}"><i class="bi ${x.icone ?? 'bi-box-arrow-up-right'} me-2" aria-hidden="true"></i>${x.texto}</button></li>`).join('')}
        </ul>`;

    menu.addEventListener('click', ev => {
        const item = ev.target.closest('[data-exportar], [data-exportar-extra]');
        if (!item) return;
        if (item.dataset.exportar !== undefined) {
            tabela.button(parseInt(item.dataset.exportar, 10)).trigger();
        } else {
            extras[parseInt(item.dataset.exportarExtra, 10)].acao();
        }
    });

    const container = document.getElementById('botoes-exportacao-' + tabela.table().node().id);
    if (container) {
        container.replaceChildren(menu);
    } else {
        barraDaLista(tabela).append(menu);
    }

    // Registro de auditoria — nunca bloqueia a exportação em si: se o
    // log falhar (rede etc.), o usuário ainda consegue exportar/imprimir.
    tabela.on('buttons-action.dt', function (e, buttonApi, dt, node, config) {
        var formato = (config && config.extend) ? config.extend : 'desconhecido';
        fetch('/api/exportacoes', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({ tela: nomeTela, formato: formato })
        }).catch(function (err) {
            console.warn('Não foi possível registrar a exportação:', err);
        });
    });
}

/**
 * Ações de uma linha: até 2 botões à vista + "⋯" com o resto. Excluir e
 * ações raras vão sempre no "⋯" (evita clique acidental e deixa a linha
 * leve). Os itens mantêm as classes e data-* de antes, então os
 * listeners das telas (closest('button') / .on('click', '.classe'))
 * continuam funcionando. O "⋯" é um <a role="button"> de propósito:
 * os listeners que pegam closest('button') e desabilitam o botão durante
 * a ação não podem pegar o "⋯" (o Bootstrap não abre menu desabilitado).
 *
 * visiveis/menu: [{ classe, icone, texto, titulo?, cor?, dados?: {id: …}, perigo?, divisor?, mostrarTexto? }]
 */
function acoesDaLinha(visiveis = [], menu = []) {
    const atributos = dados => Object.entries(dados ?? {})
        .map(([k, v]) => `data-${k.replace(/[A-Z]/g, m => '-' + m.toLowerCase())}="${escaparHtml(String(v ?? ''))}"`)
        .join(' ');

    const botoes = visiveis.filter(Boolean).map(b => `
        <button type="button" class="btn btn-sm btn-outline-${b.cor ?? 'secondary'} ${b.classe}" ${atributos(b.dados)}
                title="${escaparHtml(b.titulo ?? b.texto)}" aria-label="${escaparHtml(b.titulo ?? b.texto)}">
            <i class="bi ${b.icone}" aria-hidden="true"></i>${b.mostrarTexto ? ` ${escaparHtml(b.texto)}` : ''}
        </button>`).join('');

    // Divisor só entre itens (nunca no começo, no fim ou repetido).
    const itens = menu.filter(Boolean).filter((item, n, lista) =>
        !item.divisor || (n > 0 && n < lista.length - 1 && !lista[n - 1].divisor));
    const maisAcoes = itens.length === 0 ? '' : `
        <div class="dropdown d-inline-block">
            <a href="#" role="button" class="btn btn-sm btn-outline-secondary btn-mais-acoes" data-bs-toggle="dropdown" aria-expanded="false"
               data-bs-popper-config='{"strategy":"fixed"}' title="Mais ações" aria-label="Mais ações">
                <i class="bi bi-three-dots" aria-hidden="true"></i>
            </a>
            <ul class="dropdown-menu dropdown-menu-end">
                ${itens.map(i => i.divisor
                    ? '<li><hr class="dropdown-divider"></li>'
                    : `<li><button type="button" class="dropdown-item ${i.perigo ? 'text-danger' : ''} ${i.classe}" ${atributos(i.dados)}>
                           <i class="bi ${i.icone} me-2" aria-hidden="true"></i>${escaparHtml(i.texto)}
                       </button></li>`).join('')}
            </ul>
        </div>`;

    return `<div class="acoes-linha">${botoes}${maisAcoes}</div>`;
}
