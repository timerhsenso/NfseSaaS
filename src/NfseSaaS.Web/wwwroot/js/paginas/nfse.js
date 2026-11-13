const podeEmitirNfse = document.getElementById('btn-emitir-nfse') !== null;
const podeCancelarNfse = document.getElementById('btn-cancelar-nfse') !== null;

// NfseStatus (Domain.Enums) é serializado como número pela API — mapeado
// aqui pro rótulo/cor exibidos. Manter em sincronia se o enum mudar.
const ROTULOS_STATUS = {
    0: { texto: 'Rascunho', cor: 'secondary' },
    1: { texto: 'Processando', cor: 'info' },
    2: { texto: 'Autorizada', cor: 'success' },
    3: { texto: 'Rejeitada', cor: 'danger' },
    4: { texto: 'Cancelada', cor: 'dark' },
    5: { texto: 'Substituída', cor: 'secondary' }
};
const STATUS_AUTORIZADA = 2;
const STATUS_CANCELADA = 4;

let tabelaNfse;
let modalEmitirNfse;
let modalDetalheNfse;
let modalMotivoCancelamento;
let empresaAtualIdNfse;
let clientesPorId = {};
let nfseDetalheAtualId;
// Id da nota Rejeitada sendo reenviada pelo modal de emissão (null =
// emissão normal). Ver abrirModalReenvioNfse.
let nfseReenvioAtualId = null;
// C7: nota autorizada sendo substituída (null = emissão comum ou reenvio).
let nfseSubstituicaoAtual = null;
let modalNotaMensal;
let candidatosNotaMensal = []; // estado local da grade (selecionado, valores por serviço, status e mensagem por linha)
let nfseSelecionadas = new Set(); // ids marcados pra download em lote — sobrevive a paginação/redraw do DataTable, só é limpo quando os filtros mudam
// E-mail da nota: modal de uma nota, modal do lote e notas emitidas pela Nota Mensal nesta abertura.
let modalEmailNfse = null;
let modalEmailLote = null;
let nfseIdEmailAtual = null;
let idsEmailLote = [];
let nmNfseIdsEmitidas = [];
// C6: descontos e retenções do modal de emissão — última simulação do
// servidor (fonte única do cálculo) e controle de chamadas fora de ordem.
let simulacaoRetencoes = null;
let timerRetencoes = null;
let sequenciaRetencoes = 0;
const TRIBUTOS_RETENCAO = ['irrf', 'pis', 'cofins', 'csll', 'inss'];

document.addEventListener('DOMContentLoaded', function () {
    tabelaNfse = new DataTable('#tabela-nfse', {
        columns: [
            {
                data: null,
                orderable: false,
                render: (d, t, n) => `<input type="checkbox" class="form-check-input check-linha-nfse" data-id="${n.id}" ${nfseSelecionadas.has(n.id) ? 'checked' : ''}>`
            },
            {
                // Número oficial da NFS-e (quando autorizada) e, embaixo, a DPS que a originou.
                data: null,
                className: 'col-curta',
                render: (d, t, n) => t !== 'display'
                    ? `${n.numeroNfse ?? ''} ${n.numeroDps}`
                    : n.numeroNfse
                        ? `<strong>${escaparHtml(n.numeroNfse)}</strong> <br><small class="text-muted">DPS ${n.numeroDps}/${escaparHtml(n.serieDps)}</small>`
                        : `<span class="text-muted">DPS ${n.numeroDps}/${escaparHtml(n.serieDps)}</span>`
            },
            {
                // Apelido na grade; razão social na dica (title) do mouse.
                data: 'clienteId',
                render: (id, t, n) => {
                    const c = clientesPorId[id];
                    const nome = c ? `<span title="${escaparHtml(c.nome)}">${escaparHtml(c.apelido)}</span>` : id;
                    return t === 'display' ? nome + envelopeEmailNfse(n) : (c ? c.apelido : id);
                }
            },
            {
                // Valor do serviço; o líquido aparece embaixo só quando é diferente (retenções).
                data: 'valorServico', className: 'col-valor',
                render: (v, t, n) => {
                    if (t !== 'display') return Number(v);
                    const liquido = n.valorLiquido != null && Number(n.valorLiquido) !== Number(v)
                        ? `<br><small class="text-muted">líquido ${formatarMoedaNfse(n.valorLiquido)}</small>`
                        : '';
                    return `${formatarMoedaNfse(v)}${liquido}`;
                }
            },
            {
                data: 'status', className: 'col-curta',
                render: (v, t, n) => {
                    const r = ROTULOS_STATUS[v] ?? { texto: 'Desconhecido', cor: 'secondary' };
                    const reenviada = n.reenviadaComoNumeroDps != null
                        ? `<br><small class="text-muted">Reenviada → nº ${n.reenviadaComoNumeroDps}</small>`
                        : '';
                    return `<span class="badge text-bg-${r.cor}">${r.texto}</span>${reenviada}`;
                }
            },
            {
                data: 'dataEmissao', className: 'col-curta',
                render: {
                    // "display" é só o que aparece (data em cima, hora menor
                    // embaixo); "sort" é o valor real (timestamp) usado pra
                    // ordenar — o texto "DD/MM/AAAA" ordenaria errado.
                    display: v => {
                        if (!v) return '—';
                        const d = new Date(v);
                        return `${d.toLocaleDateString('pt-BR')}<br><small class="text-muted">${d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</small>`;
                    },
                    sort: v => v ? new Date(v).getTime() : 0,
                    _: v => v ? new Date(v).toLocaleString('pt-BR') : ''
                }
            },
            {
                data: null,
                orderable: false,
                render: (d, t, n) => {
                    const temPdf = n.status === STATUS_AUTORIZADA || n.status === STATUS_CANCELADA;
                    // podeReenviar já vem resolvido pelo backend (rejeição
                    // segura de reenviar + ainda não reenviada) — a tela
                    // só soma a permissão de emitir. Na rejeitada, reenviar
                    // é O que se faz: fica à vista.
                    const reenviar = podeEmitirNfse && n.podeReenviar;
                    const dados = { id: n.id };
                    return acoesDaLinha(
                        [
                            temPdf && { classe: 'btn-pdf-nfse', icone: 'bi-file-earmark-pdf', texto: 'Baixar PDF (DANFSe)', dados },
                            reenviar && { classe: 'btn-reenviar-nfse', icone: 'bi-arrow-repeat', texto: 'Reenviar', titulo: 'Reenviar com os dados atuais do cadastro', cor: 'warning', mostrarTexto: true, dados }
                        ],
                        [
                            { classe: 'btn-ver-detalhe', icone: 'bi-eye', texto: 'Detalhes', dados },
                            temPdf && { classe: 'btn-xml-nfse', icone: 'bi-filetype-xml', texto: 'Baixar XML', dados },
                            // Manda (de novo) o PDF e o XML pro e-mail do tomador.
                            podeEmitirNfse && n.status === STATUS_AUTORIZADA && { classe: 'btn-enviar-email-nfse', icone: 'bi-envelope', texto: 'Enviar por e-mail ao cliente', dados },
                            // C7: substituir = emitir a nota certa e a SEFIN cancela esta (permissão de cancelar).
                            podeCancelarNfse && n.podeSubstituir && { divisor: true },
                            podeCancelarNfse && n.podeSubstituir && { classe: 'btn-substituir-nfse', icone: 'bi-arrow-left-right', texto: 'Substituir (corrigir) nota', dados }
                        ]);
                }
            }
        ],
        // Sem isso, o DataTables cai no default (1ª coluna, checkbox,
        // não ordenável) — a API já manda mais recente primeiro, mas a
        // tabela reordenava na tela por cima disso. Índice 5 = coluna
        // "Emissão" (0-based: checkbox, número, cliente, valor, status,
        // emissão, ações).
        order: [[5, 'desc']],
        // Clicar na linha (fora de botão/checkbox) abre os Detalhes.
        createdRow: tr => tr.classList.add('linha-clicavel'),
    });

    // XML do mês também leva dados para fora: fica no menu Exportar.
    configurarExportacao(tabelaNfse, 'NotasFiscais', {
        extras: [{ texto: 'XML do mês (para o contador)', icone: 'bi-filetype-xml', acao: abrirXmlMes }]
    });

    document.getElementById('tabela-nfse').addEventListener('click', async function (e) {
        const pdf = e.target.closest('.btn-pdf-nfse');
        if (pdf) window.location.href = `/api/nfse/${pdf.dataset.id}/danfse-pdf`;

        const xml = e.target.closest('.btn-xml-nfse');
        if (xml) window.location.href = `/api/nfse/${xml.dataset.id}/xml`;

        // Clique na linha (não em botão, menu, link ou checkbox) = Detalhes.
        const linha = e.target.closest('tr.linha-clicavel');
        if (linha && !e.target.closest('button, a, input, .dropdown-menu')) {
            const dadosLinha = tabelaNfse.row(linha).data();
            if (dadosLinha) await abrirDetalheNfse(dadosLinha.id);
            return;
        }

        const botao = e.target.closest('.btn-ver-detalhe');
        if (botao) await executarComBotaoDesabilitado(botao, () => abrirDetalheNfse(botao.dataset.id));

        const botaoReenviar = e.target.closest('.btn-reenviar-nfse');
        if (botaoReenviar) await executarComBotaoDesabilitado(botaoReenviar, () => abrirModalReenvioNfse(botaoReenviar.dataset.id));

        const botaoSubstituir = e.target.closest('.btn-substituir-nfse');
        if (botaoSubstituir) await executarComBotaoDesabilitado(botaoSubstituir, () => abrirModalSubstituicaoNfse(botaoSubstituir.dataset.id));

        const botaoEmail = e.target.closest('.btn-enviar-email-nfse');
        if (botaoEmail) await executarComBotaoDesabilitado(botaoEmail, () => abrirModalEmailNfse(botaoEmail.dataset.id));
    });

    document.getElementById('tabela-nfse').addEventListener('change', function (e) {
        if (!e.target.classList.contains('check-linha-nfse')) return;
        const id = e.target.dataset.id;
        if (e.target.checked) nfseSelecionadas.add(id); else nfseSelecionadas.delete(id);
        atualizarBotaoBaixarSelecionadas();
    });

    document.getElementById('check-selecionar-todas-nfse').addEventListener('change', function (e) {
        // Seleciona/desmarca TODAS as notas já carregadas (até 200,
        // pageSize atual), não só as da página visível no momento —
        // senão trocar de página no DataTable perderia a marcação.
        const todosIds = tabelaNfse.rows().data().toArray().map(n => n.id);
        if (e.target.checked) todosIds.forEach(id => nfseSelecionadas.add(id));
        else todosIds.forEach(id => nfseSelecionadas.delete(id));
        tabelaNfse.draw(false); // false = mantém a página atual, só redesenha os checkboxes
        atualizarBotaoBaixarSelecionadas();
    });

    document.getElementById('btn-baixar-selecionadas').addEventListener('click', baixarDanfsePdfLote);
    configurarEmailNfse();
    document.getElementById('btn-desmarcar-nfse').addEventListener('click', function () {
        nfseSelecionadas.clear();
        document.getElementById('check-selecionar-todas-nfse').checked = false;
        tabelaNfse.draw(false);
        atualizarBotaoBaixarSelecionadas();
    });
    document.getElementById('form-xml-mes').addEventListener('submit', baixarXmlMes);

    modalDetalheNfse = new bootstrap.Modal(document.getElementById('modal-detalhe-nfse'));

    // Troca ícone/texto do botão conforme o snapshot é expandido/ocultado
    // — só precisa de um listener (o elemento do modal fica fixo no DOM,
    // só o conteúdo é repopulado a cada abertura, ver abrirDetalheNfse).
    const snapshotCollapse = document.getElementById('detalhe-nfse-snapshot-collapse');
    const btnToggleSnapshot = document.getElementById('btn-toggle-snapshot');
    snapshotCollapse.addEventListener('show.bs.collapse', function () {
        btnToggleSnapshot.innerHTML = '<i class="bi bi-chevron-up"></i> Ocultar';
    });
    snapshotCollapse.addEventListener('hide.bs.collapse', function () {
        btnToggleSnapshot.innerHTML = '<i class="bi bi-chevron-down"></i> Mostrar';
    });

    if (podeEmitirNfse) {
        modalEmitirNfse = new bootstrap.Modal(document.getElementById('modal-emitir-nfse'));
        document.getElementById('btn-emitir-nfse').addEventListener('click', abrirModalEmitirNfse);
        document.getElementById('form-emitir-nfse').addEventListener('submit', emitirNfse);
        document.getElementById('btn-sincronizar-sefin').addEventListener('click', sincronizarComSefin);

        // Mesmo padrão do clienteId em contratos.js: dropdownParent pra
        // não nascer fora do modal, minimumInputLength pra não carregar
        // a lista de clientes inteira (chegava a 200) só de abrir o
        // campo. Trocar de cliente busca os Contratos dele — ver
        // carregarContratosDoCliente, chamado pelo 'change' nativo que
        // o select2 dispara no <select> por baixo.
        $('#emitir-clienteId').select2({
            dropdownParent: $('#modal-emitir-nfse'),
            placeholder: 'Digite o apelido, nome ou CNPJ/CPF do cliente',
            minimumInputLength: 2,
            language: {
                inputTooShort: () => 'Digite pelo menos 2 caracteres para buscar.',
                searching: () => 'Buscando…',
                noResults: () => 'Nenhum cliente encontrado.'
            },
            ajax: {
                url: '/api/clientes',
                dataType: 'json',
                delay: 300,
                data: params => ({ empresaId: empresaAtualIdNfse, busca: params.term, pageSize: 20 }),
                processResults: data => ({
                    // retemIss vai junto no item do select2 pra marcar
                    // "ISS retido" com o padrão do Cliente ao escolhê-lo.
                    results: data.items.map(c => ({ id: c.id, text: `${c.apelido} (${c.cpfCnpj})`, retemIss: c.retemIss }))
                })
            }
        });
        // Tem que ser .on() do jQuery, NÃO addEventListener: o select2
        // dispara o 'change' via jQuery .trigger(), que só chama handlers
        // registrados pelo jQuery — um listener nativo nunca era chamado,
        // e por isso os Contratos do Cliente não carregavam nunca.
        $('#emitir-clienteId').on('change', function () {
            // No reenvio, Cliente e Contrato ficam travados nos da nota
            // original — buscar os Contratos aqui sobrescreveria Serviço/
            // valor/descrição com os do Contrato (aplicarContratoSelecionado).
            if (nfseReenvioAtualId || nfseSubstituicaoAtual) return;
            const selecionado = $('#emitir-clienteId').select2('data')[0];
            definirIssRetido(selecionado?.retemIss === true);
            carregarContratosDoCliente(this.value);
            carregarRetencoesPadraoCliente(this.value);
        });

        configurarSelectMunicipio({ select: 'emitir-localPrestacao', modal: 'modal-emitir-nfse' });

        document.getElementById('emitir-issRetido').addEventListener('change', function () {
            definirIssRetido(this.checked);
            agendarRetencoesEmissao();
        });

        // C6: descontos e retenções.
        document.getElementById('btn-toggle-retencoes').addEventListener('click', function () {
            abrirBlocoRetencoes(document.getElementById('corpo-retencoes').classList.contains('d-none'));
        });
        document.querySelectorAll('.check-retencao').forEach(check => check.addEventListener('change', function () {
            const percentual = document.getElementById(`emitir-ret-${this.dataset.tributo}-percentual`);
            percentual.disabled = !this.checked;
            if (this.checked && percentual.value === '') percentual.value = percentual.dataset.padrao;
            agendarRetencoesEmissao();
        }));
        ['emitir-descontoIncondicionado', 'emitir-descontoCondicionado', 'emitir-aliquotaIss', 'emitir-valorServico']
            .forEach(id => document.getElementById(id).addEventListener('input', agendarRetencoesEmissao));
        document.querySelectorAll('.percentual-retencao').forEach(campo => campo.addEventListener('input', agendarRetencoesEmissao));
        document.getElementById('emitir-reterAbaixoMinimo').addEventListener('change', agendarRetencoesEmissao);

        // Digitou = é do usuário: o preenchimento automático não mexe mais.
        document.getElementById('emitir-descricaoServico').addEventListener('input', function () {
            descricaoEditadaPeloUsuario = true;
            atualizarAvisoDescricaoEmissao();
        });
        document.getElementById('emitir-valorServico').addEventListener('input', function () {
            valorEditadoPeloUsuario = true;
            atualizarAvisoDescricaoEmissao();
        });
        document.getElementById('link-usar-descricao-sugerida').addEventListener('click', function (e) {
            e.preventDefault();
            descricaoEditadaPeloUsuario = false;
            valorEditadoPeloUsuario = false;
            aplicarSugestaoEmissao(sugestaoEmissao.descricao, sugestaoEmissao.valor);
        });
        document.getElementById('btn-voltar-emissao').addEventListener('click', mostrarCamposEmissao);

        modalNotaMensal = new bootstrap.Modal(document.getElementById('modal-nota-mensal'));
        document.getElementById('btn-nota-mensal').addEventListener('click', abrirModalNotaMensal);
        document.getElementById('btn-buscar-candidatos-nota-mensal').addEventListener('click', buscarCandidatosNotaMensal);
        document.getElementById('btn-emitir-nota-mensal').addEventListener('click', emitirNotaMensal);
        document.getElementById('btn-nm-repetir-ultimos').addEventListener('click', repetirUltimosValoresNotaMensal);
        document.getElementById('btn-nm-gravar-todos').addEventListener('click', gravarTodosNoContratoNotaMensal);
        const corpoNotaMensal = document.getElementById('linhas-nota-mensal');
        corpoNotaMensal.addEventListener('input', function (e) {
            if (e.target.classList.contains('nm-valor-servico'))
                aoMudarValorServicoNotaMensal(Number(e.target.dataset.i), Number(e.target.dataset.j), e.target.value);
        });
        corpoNotaMensal.addEventListener('click', function (e) {
            const usar = e.target.closest('.nm-usar-ultimo');
            if (!usar) return;
            e.preventDefault();
            const i = Number(usar.dataset.i), j = Number(usar.dataset.j);
            const s = candidatosNotaMensal[i].valores[j];
            const campo = corpoNotaMensal.querySelector(`.nm-valor-servico[data-i="${i}"][data-j="${j}"]`);
            campo.value = s.ultimoValor;
            aoMudarValorServicoNotaMensal(i, j, s.ultimoValor);
        });
        corpoNotaMensal.addEventListener('change', function (e) {
            if (e.target.classList.contains('nm-gravar'))
                candidatosNotaMensal[e.target.dataset.i].valores[e.target.dataset.j].gravar = e.target.checked;
            if (e.target.classList.contains('nm-check') || e.target.classList.contains('nm-valor-servico')) atualizarBotaoEmitirNotaMensal();
        });
    }

    if (podeCancelarNfse) {
        modalMotivoCancelamento = new bootstrap.Modal(document.getElementById('modal-motivo-cancelamento'));
        document.getElementById('btn-cancelar-nfse').addEventListener('click', function () {
            modalDetalheNfse.hide();
            document.getElementById('erro-cancelar-nfse').classList.add('d-none');
            document.getElementById('form-cancelar-nfse').reset();
            modalMotivoCancelamento.show();
        });
        document.getElementById('form-cancelar-nfse').addEventListener('submit', confirmarCancelamentoNfse);
    }

    empresaAtualIdNfse = obterEmpresaAtualId();

    // Link do e-mail de "revisão" da automação de Nota Mensal
    // (?abrirNotaMensal=1&competencia=yyyy-MM) — se empresa-atual.js
    // tiver disparado um reload nesta mesma carga por causa de
    // ?empresaId=... na URL, este trecho nem chega a rodar agora; roda
    // de novo na carga seguinte, já com a Empresa certa.
    if (podeEmitirNfse) {
        const paramsUrl = new URLSearchParams(location.search);
        if (paramsUrl.get('abrirNotaMensal') === '1') {
            abrirModalNotaMensal(paramsUrl.get('competencia'));
            history.replaceState(null, '', location.pathname);
        }
    }

    // Período: atalhos (padrão "Este mês"); as datas só aparecem no
    // "Personalizado". Os campos de data continuam sendo o que vai pra API.
    const periodo = document.getElementById('filtro-periodo');
    aplicarPeriodoNfse(periodo.value);
    periodo.addEventListener('change', function () {
        aplicarPeriodoNfse(this.value);
        carregarNfse();
    });

    ['filtro-data-inicio', 'filtro-data-fim', 'filtro-status', 'filtro-cliente'].forEach(id =>
        document.getElementById(id).addEventListener('change', carregarNfse));

    document.getElementById('btn-limpar-filtros-nfse').addEventListener('click', function () {
        periodo.value = 'este-mes';
        aplicarPeriodoNfse('este-mes');
        document.getElementById('filtro-status').value = '';
        document.getElementById('filtro-cliente').value = '';
        carregarNfse();
    });

    (async function () {
        await carregarClientesParaMapa();
        await carregarNfse();
    })();
});

async function carregarClientesParaMapa() {
    if (!empresaAtualIdNfse) return;
    try {
        const resultado = await apiFetch(`/api/clientes?empresaId=${empresaAtualIdNfse}&pageSize=200&incluirInativos=true`);
        clientesPorId = {};
        for (const c of resultado.items) clientesPorId[c.id] = { apelido: c.apelido, nome: c.nome, retemIss: c.retemIss };

        const selectFiltroCliente = document.getElementById('filtro-cliente');
        selectFiltroCliente.innerHTML = '<option value="">Todos os clientes</option>' +
            resultado.items.map(c => `<option value="${c.id}">${escaparHtml(c.apelido)}</option>`).join('');
    } catch (err) {
        mostrarErro(err.message);
    }
}

function montarQueryFiltrosNfse() {
    const params = new URLSearchParams();
    params.set('empresaId', empresaAtualIdNfse);
    params.set('pageSize', '200');

    const dataInicio = document.getElementById('filtro-data-inicio').value;
    const dataFim = document.getElementById('filtro-data-fim').value;
    const status = document.getElementById('filtro-status').value;
    const clienteId = document.getElementById('filtro-cliente').value;

    if (dataInicio) params.set('dataInicio', dataInicio);
    if (dataFim) params.set('dataFim', dataFim);
    if (status !== '') params.set('status', status);
    if (clienteId) params.set('clienteId', clienteId);

    return params.toString();
}

// Datas De/Até a partir do atalho de período (datas locais, sem fuso).
function aplicarPeriodoNfse(valor) {
    const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const hoje = new Date();
    const inicio = document.getElementById('filtro-data-inicio');
    const fim = document.getElementById('filtro-data-fim');
    const personalizado = valor === 'personalizado';

    document.getElementById('filtro-periodo-personalizado').classList.toggle('d-none', !personalizado);
    document.getElementById('filtro-periodo-personalizado').classList.toggle('d-flex', personalizado);
    if (personalizado) {
        if (!inicio.value) inicio.value = iso(new Date(hoje.getFullYear(), hoje.getMonth(), 1));
        if (!fim.value) fim.value = iso(hoje);
        return;
    }

    const intervalos = {
        'este-mes': [new Date(hoje.getFullYear(), hoje.getMonth(), 1), hoje],
        'mes-passado': [new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1), new Date(hoje.getFullYear(), hoje.getMonth(), 0)],
        '30-dias': [new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - 30), hoje],
        'este-ano': [new Date(hoje.getFullYear(), 0, 1), hoje]
    };
    const [de, ate] = intervalos[valor] ?? intervalos['este-mes'];
    inicio.value = iso(de);
    fim.value = iso(ate);
}

// "Limpar filtros" só aparece quando algum filtro saiu do padrão.
function atualizarLimparFiltrosNfse() {
    const ativo = document.getElementById('filtro-periodo').value !== 'este-mes'
        || document.getElementById('filtro-status').value !== ''
        || document.getElementById('filtro-cliente').value !== '';
    document.getElementById('btn-limpar-filtros-nfse').classList.toggle('d-none', !ativo);
}

function formatarMoedaNfse(v) {
    return Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

async function carregarNfse() {
    if (!empresaAtualIdNfse) return;
    atualizarLimparFiltrosNfse();
    try {
        const resultado = await apiFetch(`/api/nfse?${montarQueryFiltrosNfse()}`);
        nfseSelecionadas.clear();
        document.getElementById('check-selecionar-todas-nfse').checked = false;
        atualizarBotaoBaixarSelecionadas();
        tabelaNfse.clear();
        tabelaNfse.rows.add(resultado.items);
        tabelaNfse.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

// SituacaoContrato (Domain.Enums) serializado como número — mesmo padrão
// de ROTULOS_STATUS acima, replicado aqui e em contratos.js (custo aceito
// de não ter como compartilhar entre C# e JS sem gerar código).
const ROTULOS_SITUACAO_CONTRATO = {
    0: { texto: 'em dia', classe: 'alert-success' },
    1: { texto: 'vencendo em breve', classe: 'alert-warning' },
    2: { texto: 'vencido', classe: 'alert-danger' }
};
const ROTULOS_INDICE_NFSE = { 0: 'IPCA', 1: 'IGP-M', 2: 'INCC', 3: 'INPC', 4: 'Selic', 5: 'CDI', 99: 'Outro' };

let contratosDoClienteAtual = [];

// Proteção do que o usuário digitou no modal de emissão: o preenchimento
// automático (troca de serviço ou contrato, e a busca dos contratos do
// cliente, que responde DEPOIS) nunca sobrescreve descrição/valor editados
// à mão — antes sobrescrevia em silêncio e dava para emitir com o texto
// errado. A sugestão fica guardada para o link "usar a do cadastro".
let descricaoEditadaPeloUsuario = false;
let valorEditadoPeloUsuario = false;
let sugestaoEmissao = { descricao: '', valor: '' };

// Emissão em 2 etapas: "Revisar" mostra o resumo, "Confirmar e emitir" envia.
let etapaResumoEmissao = false;

// Uma chave por abertura do modal (não por clique): repetir o envio depois
// de um erro de rede reaproveita a mesma tentativa na API, sem duplicar nota.
let chaveIdempotenciaEmissao = null;

async function abrirModalEmitirNfse() {
    if (!empresaAtualIdNfse) {
        mostrarToast('Cadastre a sua empresa em Empresas antes de emitir notas.', 'erro');
        return;
    }
    try {
        await prepararModalEmitirNfse();
        modalEmitirNfse.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

// Reenvio de nota Rejeitada: mesmo modal de emissão, pré-preenchido com
// Serviço/valor/descrição/competência da tentativa rejeitada. Cliente e
// Contrato travados (o backend exige os mesmos da original). Empresa,
// Cliente e Serviço são relidos do cadastro ATUAL no backend ao emitir —
// é isso que faz a correção feita no cadastro (ex.: CEP) valer.
async function abrirModalReenvioNfse(id) {
    try {
        const nfse = await apiFetch(`/api/nfse/${id}`);
        await prepararModalEmitirNfse();

        nfseReenvioAtualId = nfse.id;
        document.getElementById('titulo-modal-emitir-nfse').textContent = `Reenviar nota rejeitada — DPS nº ${nfse.numeroDps}`;
        mostrarCamposEmissao();

        // Montado via textContent: a mensagem vem da SEFIN/validação,
        // nunca entra no innerHTML.
        const aviso = document.getElementById('aviso-reenvio-nfse');
        aviso.replaceChildren();
        const titulo = document.createElement('strong');
        titulo.textContent = 'Erro da tentativa anterior: ';
        const erro = document.createElement('span');
        erro.textContent = `${nfse.codigoErro ?? ''} — ${nfse.mensagemErro ?? 'motivo não informado'}`;
        const dica = document.createElement('div');
        dica.className = 'small mt-1';
        dica.textContent = nfse.rejeicao
            ? `${nfse.rejeicao.ondeCorrigir}: ${nfse.rejeicao.oQueFazer} Empresa, Cliente e Serviço serão lidos novamente do cadastro atual.`
            : 'Empresa, Cliente e Serviço serão lidos novamente do cadastro atual. Se o erro veio do cadastro, corrija-o antes de reenviar.';
        aviso.append(titulo, erro, dica);
        aviso.classList.remove('d-none');

        // Cliente: select2 com fonte AJAX não conhece o texto de um Id
        // só pelo valor — recria a <option> (mesmo padrão dos catálogos).
        const selectCliente = document.getElementById('emitir-clienteId');
        selectCliente.append(new Option(clientesPorId[nfse.clienteId]?.apelido ?? nfse.clienteId, nfse.clienteId, true, true));
        $('#emitir-clienteId').prop('disabled', true).trigger('change');

        const campoContrato = document.getElementById('campo-emitir-contrato');
        const selectContrato = document.getElementById('emitir-contratoId');
        if (nfse.contratoId) {
            selectContrato.innerHTML = `<option value="${nfse.contratoId}">Mesmo contrato da nota original</option>`;
            selectContrato.disabled = true;
            campoContrato.classList.remove('d-none');
        }

        // Serviço da nota original, se ainda existir no catálogo; senão
        // (nota antiga, sem ServicoId, ou Serviço excluído/inativo) o
        // usuário escolhe — o campo é obrigatório.
        const selectServico = document.getElementById('emitir-servicoId');
        const servicoExiste = nfse.servicoId && [...selectServico.options].some(o => o.value === nfse.servicoId);
        if (servicoExiste) {
            selectServico.value = nfse.servicoId;
        } else {
            selectServico.insertBefore(new Option('Selecione o serviço…', '', true, true), selectServico.firstChild);
        }

        document.getElementById('emitir-descricaoServico').value = nfse.descricaoServico;
        document.getElementById('emitir-valorServico').value = nfse.valorServico;
        // Texto e valor da nota original contam como "do usuário": trocar o
        // serviço no reenvio não os sobrescreve.
        sugestaoEmissao = { descricao: nfse.descricaoServico, valor: String(nfse.valorServico) };
        descricaoEditadaPeloUsuario = true;
        valorEditadoPeloUsuario = true;
        atualizarAvisoDescricaoEmissao();
        document.getElementById('emitir-dataCompetencia').value = nfse.dataCompetencia;
        // Reenvio relê o cadastro atual (ver aviso acima): retenção pelo
        // padrão ATUAL do Cliente — corrigir o cadastro e reenviar funciona.
        definirIssRetido(clientesPorId[nfse.clienteId]?.retemIss === true);
        // Retenções federais: também pelo padrão ATUAL do Cliente; descontos, os da tentativa.
        definirDescontosNaTela(nfse.descontosRetencoes);
        await carregarRetencoesPadraoCliente(nfse.clienteId);
        // Local da prestação: o da tentativa rejeitada (dá para trocar).
        await definirMunicipioNoSelect('emitir-localPrestacao', nfse.codigoMunicipioPrestacao);

        modalEmitirNfse.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

// Deixa o modal de emissão limpo (modo emissão normal) e com a lista de
// Serviços carregada. Não abre o modal — quem chama decide.
async function prepararModalEmitirNfse() {
    nfseReenvioAtualId = null;
    nfseSubstituicaoAtual = null;
    document.getElementById('bloco-substituicao-nfse').classList.add('d-none');
    ['emitir-dataCompetencia', 'emitir-valorServico', 'emitir-servicoId'].forEach(id => { document.getElementById(id).disabled = false; });
    $('#emitir-localPrestacao').prop('disabled', false);
    document.getElementById('titulo-modal-emitir-nfse').textContent = 'Emitir nota fiscal';
    descricaoEditadaPeloUsuario = false;
    valorEditadoPeloUsuario = false;
    sugestaoEmissao = { descricao: '', valor: '' };
    chaveIdempotenciaEmissao = crypto.randomUUID();
    mostrarCamposEmissao();
    document.getElementById('aviso-reenvio-nfse').classList.add('d-none');
    document.getElementById('emitir-contratoId').disabled = false;
    $('#emitir-clienteId').prop('disabled', false);

    document.getElementById('form-emitir-nfse').reset();
    document.getElementById('erro-emitir-nfse').classList.add('d-none');
    document.getElementById('emitir-dataCompetencia').value = dataLocalIso();
    definirIssRetido(false);
    limparRetencoesEmissao();
    await definirMunicipioNoSelect('emitir-localPrestacao', null);

    // select2 não some sozinho no form.reset() — limpa manualmente,
    // senão reabrir o modal mostra o cliente da vez anterior.
    document.getElementById('emitir-clienteId').innerHTML = '';
    $('#emitir-clienteId').val(null).trigger('change');

    const servicos = await apiFetch(`/api/servicos?empresaId=${empresaAtualIdNfse}&pageSize=200`);

    const selectServico = document.getElementById('emitir-servicoId');
    selectServico.innerHTML = servicos.items.map(s => `<option value="${s.id}" data-descricao="${escaparHtml(s.descricao)}" data-valor="${s.valorPadrao}">${escaparHtml(s.descricao)}</option>`).join('');

    // Ao trocar o serviço MANUALMENTE, pré-preenche descrição/valor.
    // Com Contrato selecionado e o serviço sendo uma linha dele, usa os
    // valores da LINHA do Contrato; caso contrário, o padrão do catálogo.
    selectServico.onchange = function () {
        const contrato = contratoSelecionado();
        const linha = contrato?.servicos.find(s => s.servicoId === selectServico.value);
        if (linha) {
            preencherComLinhaDoContrato(contrato, linha);
            return;
        }

        const opcao = selectServico.selectedOptions[0];
        aplicarSugestaoEmissao(opcao?.dataset.descricao ?? '', opcao?.dataset.valor ?? '');
    };
    atualizarAvisoDescricaoEmissao();
}

async function carregarContratosDoCliente(clienteId) {
    const campoContrato = document.getElementById('campo-emitir-contrato');
    const selectContrato = document.getElementById('emitir-contratoId');
    const selectServico = document.getElementById('emitir-servicoId');

    // Dispara com clienteId vazio quando o select2 é limpo (abrir o
    // modal, ou apagar a busca) — não é erro, só esconde o combo de
    // Contrato e volta ao fluxo 100% manual, sem bater na API à toa.
    if (!clienteId) {
        contratosDoClienteAtual = [];
        campoContrato.classList.add('d-none');
        selectContrato.innerHTML = '';
        document.getElementById('aviso-reajuste-contrato').classList.add('d-none');
        document.getElementById('aviso-contrato-multiplos-servicos').classList.add('d-none');
        return;
    }

    try {
        const resultado = await apiFetch(`/api/contratos?empresaId=${empresaAtualIdNfse}&clienteId=${clienteId}&pageSize=50`);
        contratosDoClienteAtual = resultado.items;

        if (contratosDoClienteAtual.length === 0) {
            // Sem Contrato nenhum pra este Cliente — nem mostra a opção,
            // fluxo continua 100% manual (Serviço + valor), como sempre foi.
            campoContrato.classList.add('d-none');
            selectContrato.innerHTML = '';
            document.getElementById('aviso-reajuste-contrato').classList.add('d-none');
        document.getElementById('aviso-contrato-multiplos-servicos').classList.add('d-none');
            selectServico.onchange();
            return;
        }

        campoContrato.classList.remove('d-none');
        selectContrato.innerHTML =
            '<option value="">Nenhum (nota avulsa)</option>' +
            contratosDoClienteAtual.map(c => `<option value="${c.id}">${escaparHtml(rotuloContrato(c))} — ${Number(c.valorAtual).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</option>`).join('');

        // Só 1 Contrato ativo pra este Cliente: já vem selecionado (evita
        // o retrabalho de escolher toda vez), mas continua visível e
        // trocável — pode ser uma nota avulsa fora do contrato mesmo
        // assim.
        selectContrato.value = contratosDoClienteAtual.length === 1 ? contratosDoClienteAtual[0].id : '';

        selectContrato.onchange = aplicarContratoSelecionado;
        selectContrato.onchange();
    } catch (err) {
        mostrarErro(err.message);
    }
}

// Marca/desmarca "ISS retido" e mostra a alíquota só quando retido. Ao
// desmarcar, limpa a alíquota digitada (sem retenção ela não vai na DPS).
function definirIssRetido(retido) {
    document.getElementById('emitir-issRetido').checked = retido;
    document.getElementById('campo-emitir-aliquotaIss').classList.toggle('d-none', !retido);
    if (!retido)
        document.getElementById('emitir-aliquotaIss').value = '';
    agendarRetencoesEmissao();
}

// ---------- C6: descontos e retenções federais ----------

const moedaRetencao = v => Number(v ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

function abrirBlocoRetencoes(abrir) {
    document.getElementById('corpo-retencoes').classList.toggle('d-none', !abrir);
    document.getElementById('btn-toggle-retencoes').setAttribute('aria-expanded', String(abrir));
    const icone = document.getElementById('icone-toggle-retencoes');
    icone.classList.toggle('bi-chevron-down', !abrir);
    icone.classList.toggle('bi-chevron-up', abrir);
}

function numeroDoCampo(id) {
    const valor = document.getElementById(id).value;
    return valor === '' ? null : parseFloat(valor);
}

/** Marca os tributos com percentual (nulo = não retém) e mostra o padrão nos desmarcados. */
function definirRetencoesNaTela(percentuais) {
    for (const tributo of TRIBUTOS_RETENCAO) {
        const valor = percentuais?.[tributo];
        const percentual = document.getElementById(`emitir-ret-${tributo}-percentual`);
        document.getElementById(`emitir-ret-${tributo}`).checked = valor != null;
        percentual.value = valor != null ? Number(valor).toFixed(2) : percentual.dataset.padrao;
        percentual.disabled = valor == null;
    }
}

function definirDescontosNaTela(descontos) {
    document.getElementById('emitir-descontoIncondicionado').value = descontos?.descontoIncondicionado ?? '';
    document.getElementById('emitir-descontoCondicionado').value = descontos?.descontoCondicionado ?? '';
}

function retencoesDaTela() {
    const retencoes = {};
    for (const tributo of TRIBUTOS_RETENCAO) {
        const campo = document.getElementById(`emitir-ret-${tributo}-percentual`);
        retencoes[tributo] = document.getElementById(`emitir-ret-${tributo}`).checked && campo.value !== '' ? parseFloat(campo.value) : null;
    }
    return retencoes;
}

const temAlgumaRetencao = p => !!p && TRIBUTOS_RETENCAO.some(t => p[t] != null);

const temDescontoOuRetencao = d => !!d && [d.descontoIncondicionado, d.descontoCondicionado, d.irrf, d.pis, d.cofins, d.csll, d.inss].some(v => v > 0);

/** Percentuais equivalentes aos valores retidos numa nota já emitida (base = valor − desconto incondicionado). */
function percentuaisDaNota(nfse) {
    const d = nfse.descontosRetencoes;
    const base = Number(nfse.valorServico) - Number(d?.descontoIncondicionado ?? 0);
    const percentual = v => v > 0 && base > 0 ? Math.round(v * 10000 / base) / 100 : null;
    return { irrf: percentual(d?.irrf), pis: percentual(d?.pis), cofins: percentual(d?.cofins), csll: percentual(d?.csll), inss: percentual(d?.inss) };
}

function limparRetencoesEmissao() {
    clearTimeout(timerRetencoes);
    sequenciaRetencoes++;
    simulacaoRetencoes = null;
    definirDescontosNaTela(null);
    definirRetencoesNaTela(null);
    document.getElementById('emitir-reterAbaixoMinimo').checked = false;
    abrirBlocoRetencoes(false);
    renderizarRetencoesEmissao(null);
}

/** Cliente escolhido: marca as retenções que ele faz (sem o que o regime da empresa dispensa). */
async function carregarRetencoesPadraoCliente(clienteId) {
    if (!clienteId) {
        definirRetencoesNaTela(null);
        agendarRetencoesEmissao();
        return;
    }
    const resposta = await simularRetencoes(clienteId, null);
    if (!resposta) return;
    definirRetencoesNaTela(resposta.percentuais);
    if (temAlgumaRetencao(resposta.percentuais)) abrirBlocoRetencoes(true);
    simulacaoRetencoes = resposta;
    renderizarRetencoesEmissao(resposta);
}

function agendarRetencoesEmissao() {
    clearTimeout(timerRetencoes);
    timerRetencoes = setTimeout(atualizarRetencoesEmissao, 250);
}

async function atualizarRetencoesEmissao() {
    const resposta = await simularRetencoes(document.getElementById('emitir-clienteId').value || null, retencoesDaTela());
    if (!resposta) return;
    simulacaoRetencoes = resposta;
    renderizarRetencoesEmissao(resposta);
}

/** Chama o cálculo do servidor; null = resposta antiga (já chegou uma mais nova) ou erro de rede. */
async function simularRetencoes(clienteId, retencoes) {
    if (!empresaAtualIdNfse) return null;
    const sequencia = ++sequenciaRetencoes;
    const issRetido = document.getElementById('emitir-issRetido').checked;
    try {
        const resposta = await apiFetch('/api/nfse/retencoes/simular', {
            method: 'POST',
            body: JSON.stringify({
                empresaId: empresaAtualIdNfse,
                clienteId,
                valorServico: numeroDoCampo('emitir-valorServico') ?? 0,
                descontoIncondicionado: numeroDoCampo('emitir-descontoIncondicionado'),
                descontoCondicionado: numeroDoCampo('emitir-descontoCondicionado'),
                retencoesFederais: retencoes,
                issRetido,
                aliquotaIss: issRetido ? numeroDoCampo('emitir-aliquotaIss') : null,
                reterAbaixoDoMinimo: document.getElementById('emitir-reterAbaixoMinimo').checked
            })
        });
        return sequencia === sequenciaRetencoes ? resposta : null;
    } catch (err) {
        if (sequencia !== sequenciaRetencoes) return null;
        const erro = document.getElementById('erro-retencoes');
        erro.textContent = err.message;
        erro.classList.remove('d-none');
        return null;
    }
}

function renderizarRetencoesEmissao(resposta) {
    const resultado = resposta?.resultado;
    const alertas = document.getElementById('alertas-retencoes');
    const dispensas = document.getElementById('dispensas-retencoes');
    const erro = document.getElementById('erro-retencoes');
    const tabela = document.getElementById('tabela-retencoes');
    const frase = document.getElementById('frase-dispensa-previa');

    // MEI: sem retenção federal (só descontos).
    const semFederais = resposta && resposta.retencaoFederalDisponivel === false;
    document.getElementById('retencoes-federais-campos').classList.toggle('d-none', semFederais);
    document.getElementById('aviso-mei-retencoes').classList.toggle('d-none', !semFederais);

    alertas.replaceChildren(...(resultado?.alertas ?? []).map(texto => {
        const linha = document.createElement('div');
        linha.textContent = texto;
        return linha;
    }));
    alertas.classList.toggle('d-none', !resultado?.alertas?.length);
    dispensas.textContent = (resultado?.dispensas ?? []).join(' ');
    dispensas.classList.toggle('d-none', !resultado?.dispensas?.length);
    erro.textContent = resposta?.erro ?? '';
    erro.classList.toggle('d-none', !resposta?.erro);
    frase.textContent = resposta?.fraseDispensa ? `Vai no fim da descrição: "${resposta.fraseDispensa}"` : '';
    frase.classList.toggle('d-none', !resposta?.fraseDispensa);

    const descInc = numeroDoCampo('emitir-descontoIncondicionado') ?? 0;
    const descCond = numeroDoCampo('emitir-descontoCondicionado') ?? 0;
    const temAlgo = !!resultado && (descInc > 0 || descCond > 0 || resultado.totalRetencoes > 0);
    document.getElementById('resumo-retencoes').textContent = resposta?.erro
        ? 'Revise os valores'
        : temAlgo ? `Líquido ${moedaRetencao(resultado.valorLiquido)}` : 'Nenhum';

    if (!resultado) {
        tabela.replaceChildren();
        return;
    }
    const linhas = [['Valor do serviço', numeroDoCampo('emitir-valorServico') ?? 0, false]];
    if (descInc > 0) linhas.push(['Desconto incondicionado', descInc, true]);
    if (descCond > 0) linhas.push(['Desconto condicionado', descCond, true]);
    if (resultado.issRetido > 0) linhas.push(['ISS retido', resultado.issRetido, true]);
    if (resultado.irrf > 0) linhas.push(['IRRF', resultado.irrf, true]);
    if (resultado.retidoCsllTotal > 0) linhas.push(['PIS/COFINS/CSLL', resultado.retidoCsllTotal, true]);
    if (resultado.inss > 0) linhas.push(['INSS', resultado.inss, true]);
    tabela.innerHTML = linhas.map(([rotulo, valor, menos]) =>
        `<dt class="col-7">${menos ? '(−) ' : ''}${escaparHtml(rotulo)}</dt><dd class="col-5">${moedaRetencao(valor)}</dd>`).join('')
        + `<dt class="col-7 linha-liquido">Valor líquido</dt><dd class="col-5 linha-liquido">${moedaRetencao(resultado.valorLiquido)}</dd>`;
}

/** Linhas do resumo (etapa 2): descontos, retenções federais e líquido. */
function linhasResumoRetencoes(linha) {
    const r = simulacaoRetencoes?.resultado;
    if (!r) return '';
    const descInc = numeroDoCampo('emitir-descontoIncondicionado') ?? 0;
    const descCond = numeroDoCampo('emitir-descontoCondicionado') ?? 0;
    const partes = [];
    if (r.irrf > 0) partes.push(`IRRF ${moedaRetencao(r.irrf)}`);
    if (r.retidoCsllTotal > 0) partes.push(`PIS/COFINS/CSLL ${moedaRetencao(r.retidoCsllTotal)}`);
    if (r.inss > 0) partes.push(`INSS ${moedaRetencao(r.inss)}`);
    const descontos = [];
    if (descInc > 0) descontos.push(`incondicionado ${moedaRetencao(descInc)}`);
    if (descCond > 0) descontos.push(`condicionado ${moedaRetencao(descCond)}`);
    if (!partes.length && !descontos.length && !(r.issRetido > 0)) return '';
    return (descontos.length ? linha('Descontos', escaparHtml(descontos.join(' · '))) : '')
        + linha('Retenções federais', partes.length ? escaparHtml(partes.join(' · ')) : 'Nenhuma')
        + linha('Valor líquido', `<strong>${moedaRetencao(r.valorLiquido)}</strong>`);
}

/** Avisos do regime no resumo: o usuário confirma sabendo (não bloqueia). */
function alertasResumoRetencoes() {
    const alertas = simulacaoRetencoes?.resultado?.alertas ?? [];
    if (!alertas.length) return '';
    return `<div class="alert alert-warning py-2 mt-3 mb-0">
        <strong>Atenção às retenções:</strong>
        <ul class="mb-1 ps-3">${alertas.map(a => `<li>${escaparHtml(a)}</li>`).join('')}</ul>
        <div class="small">Confirme só se o tomador realmente faz essa retenção. Na dúvida, fale com o seu contador.</div>
    </div>`;
}

/** Linhas do detalhe da nota emitida. */
function linhasDetalheRetencoes(d, linha, moeda) {
    if (!temDescontoOuRetencao(d)) return '';
    const partes = [];
    if (d.irrf > 0) partes.push(`IRRF ${moeda(d.irrf)}`);
    const csrf = [d.pis, d.cofins, d.csll].map(v => Number(v ?? 0)).reduce((a, b) => a + b, 0);
    if (csrf > 0) partes.push(`PIS/COFINS/CSLL ${moeda(csrf)}`);
    if (d.inss > 0) partes.push(`INSS ${moeda(d.inss)}`);
    return (d.descontoIncondicionado > 0 ? linha('Desconto incondicionado', moeda(d.descontoIncondicionado)) : '')
        + (d.descontoCondicionado > 0 ? linha('Desconto condicionado', moeda(d.descontoCondicionado)) : '')
        + (partes.length ? linha('Retenções federais', partes.join(' · ')) : '');
}

function contratoSelecionado() {
    const contratoId = document.getElementById('emitir-contratoId').value;
    return contratoId ? contratosDoClienteAtual.find(c => c.id === contratoId) : undefined;
}

// Contrato com 1 serviço: descrição do Contrato (comportamento de sempre).
// Com vários: descrição do serviço da linha, pra nota não descrever o
// contrato inteiro quando cobra só uma parte dele.
function preencherComLinhaDoContrato(contrato, linha) {
    aplicarSugestaoEmissao(contrato.servicos.length > 1 ? linha.servicoDescricao : contrato.descricao, linha.valorTotal);
}

/**
 * Preenche descrição e valor com o sugerido pelo serviço/contrato — só os
 * campos que o usuário ainda não editou (ou deixou vazios).
 */
function aplicarSugestaoEmissao(descricao, valor) {
    sugestaoEmissao = { descricao: descricao ?? '', valor: valor == null ? '' : String(valor) };

    const campoDescricao = document.getElementById('emitir-descricaoServico');
    if (!descricaoEditadaPeloUsuario || campoDescricao.value.trim() === '') {
        campoDescricao.value = sugestaoEmissao.descricao;
        descricaoEditadaPeloUsuario = false;
    }

    const campoValor = document.getElementById('emitir-valorServico');
    if (!valorEditadoPeloUsuario || campoValor.value === '') {
        campoValor.value = sugestaoEmissao.valor;
        valorEditadoPeloUsuario = false;
    }

    atualizarAvisoDescricaoEmissao();
    agendarRetencoesEmissao();
}

/** Contador de caracteres e aviso "você editou — usar a do cadastro". */
function atualizarAvisoDescricaoEmissao() {
    const campoDescricao = document.getElementById('emitir-descricaoServico');
    document.getElementById('contador-descricao-emissao').textContent = `${campoDescricao.value.length}/2000`;

    const campoValor = document.getElementById('emitir-valorServico');
    const descricaoDiferente = descricaoEditadaPeloUsuario && sugestaoEmissao.descricao !== '' && campoDescricao.value !== sugestaoEmissao.descricao;
    const valorDiferente = valorEditadoPeloUsuario && sugestaoEmissao.valor !== '' && Number(campoValor.value) !== Number(sugestaoEmissao.valor);
    document.getElementById('aviso-descricao-manual').classList.toggle('d-none', !(descricaoDiferente || valorDiferente));
}

/** Etapa 1 do modal: o formulário. */
function mostrarCamposEmissao() {
    etapaResumoEmissao = false;
    document.getElementById('emitir-campos').classList.remove('d-none');
    document.getElementById('emitir-resumo').classList.add('d-none');
    document.getElementById('btn-voltar-emissao').classList.add('d-none');
    document.getElementById('btn-confirmar-emissao').textContent = nfseSubstituicaoAtual ? 'Revisar substituição' : nfseReenvioAtualId ? 'Revisar reenvio' : 'Revisar';
}

/**
 * Etapa 2 do modal: resumo do que vai para a SEFIN, para conferir antes de
 * emitir (depois de autorizada, a nota só se desfaz cancelando).
 */
async function mostrarResumoEmissao() {
    // C6: o resumo usa o cálculo atualizado do servidor (sem esperar o atraso da digitação).
    clearTimeout(timerRetencoes);
    await atualizarRetencoesEmissao();
    if (simulacaoRetencoes?.erro) {
        abrirBlocoRetencoes(true);
        mostrarErroFormulario('erro-emitir-nfse', simulacaoRetencoes.erro);
        return;
    }

    const textoSelecionado = id => document.getElementById(id).selectedOptions[0]?.text ?? '';
    const cliente = $('#emitir-clienteId').select2('data')[0]?.text || textoSelecionado('emitir-clienteId');
    const campoContratoVisivel = !document.getElementById('campo-emitir-contrato').classList.contains('d-none');
    const contrato = campoContratoVisivel && document.getElementById('emitir-contratoId').value
        ? textoSelecionado('emitir-contratoId')
        : 'Nenhum (nota avulsa)';
    const valor = Number(document.getElementById('emitir-valorServico').value).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    const competencia = document.getElementById('emitir-dataCompetencia').value; // AAAA-MM-DD
    const issRetido = document.getElementById('emitir-issRetido').checked;
    const aliquota = document.getElementById('emitir-aliquotaIss').value;
    const ambiente = document.getElementById('badge-ambiente-empresa')?.textContent.trim();

    const avisoAmbiente = ambiente === 'Produção'
        ? '<div class="alert alert-warning py-2 mb-3"><strong>Produção:</strong> esta nota tem valor fiscal.</div>'
        : ambiente
            ? `<div class="alert alert-info py-2 mb-3"><strong>${escaparHtml(ambiente)}:</strong> nota de teste, sem valor fiscal.</div>`
            : '';

    const linha = (rotulo, valorHtml) => `<dt class="col-sm-4">${rotulo}</dt><dd class="col-sm-8">${valorHtml}</dd>`;

    document.getElementById('emitir-resumo').innerHTML = `
        ${avisoAmbiente}
        ${nfseSubstituicaoAtual ? `<div class="alert alert-warning py-2 mb-3">Autorizada esta nota, a SEFIN <strong>cancela a DPS nº ${nfseSubstituicaoAtual.numeroDps}</strong> automaticamente.</div>` : ''}
        <p class="mb-3">Confira antes de ${nfseSubstituicaoAtual ? 'substituir' : nfseReenvioAtualId ? 'reenviar' : 'emitir'}. Depois de autorizada, a nota só pode ser desfeita cancelando.</p>
        <dl class="row resumo-emissao mb-0">
            ${linha('Cliente', escaparHtml(cliente))}
            ${linha('Contrato', escaparHtml(contrato))}
            ${linha('Serviço', escaparHtml(textoSelecionado('emitir-servicoId')))}
            ${linha('Competência', competencia ? `${competencia.slice(5, 7)}/${competencia.slice(0, 4)}` : '—')}
            ${linha('Valor do serviço', `<strong>${valor}</strong>`)}
            ${linha('ISS', issRetido ? `Retido pelo tomador${aliquota ? ` (alíquota ${escaparHtml(aliquota)}%)` : ' (alíquota da empresa)'}` : 'Não retido')}
            ${linhasResumoRetencoes(linha)}
            ${linha('Local da prestação', escaparHtml(textoMunicipioSelecionado('emitir-localPrestacao', 'Município da empresa')))}
            ${nfseSubstituicaoAtual ? linha('Substitui', escaparHtml(`DPS nº ${nfseSubstituicaoAtual.numeroDps} — ${textoSelecionado('emitir-motivoSubstituicao')}`)) : ''}
        </dl>
        <div class="mt-2">
            <div class="form-label mb-1">Descrição que vai na nota</div>
            <div class="descricao-nota">${escaparHtml(document.getElementById('emitir-descricaoServico').value)}${simulacaoRetencoes?.fraseDispensa ? `\n${escaparHtml(simulacaoRetencoes.fraseDispensa)}` : ''}</div>
        </div>
        ${alertasResumoRetencoes()}`;

    etapaResumoEmissao = true;
    document.getElementById('emitir-campos').classList.add('d-none');
    document.getElementById('emitir-resumo').classList.remove('d-none');
    document.getElementById('btn-voltar-emissao').classList.remove('d-none');
    document.getElementById('btn-confirmar-emissao').textContent = nfseSubstituicaoAtual ? 'Confirmar e substituir' : nfseReenvioAtualId ? 'Confirmar e reenviar' : 'Confirmar e emitir';
    ocultarErroFormulario('erro-emitir-nfse');
}

function aplicarContratoSelecionado() {
    const selectContrato = document.getElementById('emitir-contratoId');
    const avisoDiv = document.getElementById('aviso-reajuste-contrato');
    const contratoId = selectContrato.value;

    if (!contratoId) {
        // "Nenhum (nota avulsa)" — volta pro fluxo manual de Serviço.
        avisoDiv.classList.add('d-none');
        definirMunicipioNoSelect('emitir-localPrestacao', null);
        document.getElementById('aviso-contrato-multiplos-servicos').classList.add('d-none');
        document.getElementById('emitir-servicoId').onchange();
        return;
    }

    const contrato = contratosDoClienteAtual.find(c => c.id === contratoId);
    if (!contrato) return;

    // Local da prestação padrão do Contrato (C2); vazio = da empresa.
    definirMunicipioNoSelect('emitir-localPrestacao', contrato.codigoMunicipioPrestacao);

    // Desde a Fase 6 o Contrato tem N linhas de serviço (contrato.servicos),
    // não mais um servicoId único — o código antigo lia contrato.servicoId
    // (undefined), deixava o Serviço em branco e a emissão caía num 400.
    // A SEFIN aceita 1 cServ por nota, então a nota avulsa sai pra UMA
    // linha: a primeira por padrão; o usuário troca no combo de Serviço.
    const linha = contrato.servicos[0];
    if (linha) {
        document.getElementById('emitir-servicoId').value = linha.servicoId;
        preencherComLinhaDoContrato(contrato, linha);
    }

    const avisoMultiplos = document.getElementById('aviso-contrato-multiplos-servicos');
    if (contrato.servicos.length > 1) {
        avisoMultiplos.textContent =
            `Este contrato tem ${contrato.servicos.length} serviços. A nota avulsa sai para o serviço selecionado abaixo ` +
            `(valor da linha do contrato). Para faturar o contrato inteiro, use "Nota Mensal".`;
        avisoMultiplos.classList.remove('d-none');
    } else {
        avisoMultiplos.classList.add('d-none');
    }

    if (contrato.situacao === 0) {
        avisoDiv.classList.add('d-none');
    } else {
        const rotulo = ROTULOS_SITUACAO_CONTRATO[contrato.situacao] ?? { texto: 'com situação desconhecida', classe: 'alert-warning' };
        const dataFormatada = new Date(contrato.dataProximoReajuste + 'T00:00:00').toLocaleDateString('pt-BR');
        avisoDiv.className = `alert ${rotulo.classe}`;
        avisoDiv.innerHTML =
            `<i class="bi bi-exclamation-triangle"></i> Este contrato está <strong>${rotulo.texto}</strong> ` +
            `pro reajuste (previsto para ${dataFormatada}${contrato.indiceReajuste != null ? ', índice ' + (ROTULOS_INDICE_NFSE[contrato.indiceReajuste] ?? '') : ''}). ` +
            (contrato.reajustePendenteId
                ? `Há um reajuste <strong>aguardando aprovação</strong> — o valor abaixo ainda é o anterior.`
                : `Confirme se o valor abaixo já está atualizado.`);
        avisoDiv.classList.remove('d-none');
    }
}

async function emitirNfse(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-emitir-nfse');

    if (!etapaResumoEmissao) {
        await mostrarResumoEmissao();
        return;
    }

    const botaoConfirmar = document.getElementById('btn-confirmar-emissao');
    botaoConfirmar.disabled = true; // emissão bate na SEFIN — evita duplo clique

    const contratoId = document.getElementById('emitir-contratoId').value;

    const payload = {
        empresaId: empresaAtualIdNfse,
        clienteId: document.getElementById('emitir-clienteId').value,
        contratoId: contratoId || null,
        servicoId: document.getElementById('emitir-servicoId').value,
        valorServico: parseFloat(document.getElementById('emitir-valorServico').value),
        descricaoServico: document.getElementById('emitir-descricaoServico').value,
        dataCompetencia: document.getElementById('emitir-dataCompetencia').value,
        // Chave de idempotência desta abertura do modal (ver
        // chaveIdempotenciaEmissao) — se o usuário confirmar de novo depois
        // de um erro de rede, a API reconhece a mesma tentativa e não
        // duplica a nota.
        idempotencyKey: chaveIdempotenciaEmissao,
        reenvioDeNfseId: nfseReenvioAtualId,
        // Sempre explícito (true/false): o que está na tela é o que vale
        // pra esta nota. Alíquota em branco = a da Empresa (backend).
        issRetido: document.getElementById('emitir-issRetido').checked,
        aliquotaIss: document.getElementById('emitir-issRetido').checked && document.getElementById('emitir-aliquotaIss').value !== ''
            ? parseFloat(document.getElementById('emitir-aliquotaIss').value)
            : null,
        // Sempre explícito: "" = município da empresa (mesmo com contrato que tenha outro padrão).
        codigoMunicipioPrestacao: $('#emitir-localPrestacao').val() || '',
        // C6: sempre explícito — o que está na tela é o que vale para esta nota.
        descontoIncondicionado: numeroDoCampo('emitir-descontoIncondicionado'),
        descontoCondicionado: numeroDoCampo('emitir-descontoCondicionado'),
        retencoesFederais: retencoesDaTela(),
        reterAbaixoDoMinimo: document.getElementById('emitir-reterAbaixoMinimo').checked
    };

    // C7: substituição vai pela rota própria, com o motivo oficial.
    const substituicao = nfseSubstituicaoAtual;
    if (substituicao) {
        payload.reenvioDeNfseId = null;
        payload.motivoSubstituicao = document.getElementById('emitir-motivoSubstituicao').value;
        payload.descricaoMotivoSubstituicao = document.getElementById('emitir-descricaoMotivoSubstituicao').value.trim() || null;
    }

    try {
        const resultado = await apiFetch(substituicao ? `/api/nfse/${substituicao.id}/substituir` : '/api/nfse/emitir',
            { method: 'POST', body: JSON.stringify(payload) });

        modalEmitirNfse.hide();
        await carregarNfse();

        if (resultado.sucesso) {
            mostrarToast(substituicao
                ? `Nota substituta autorizada. A DPS nº ${substituicao.numeroDps} foi cancelada pela SEFIN.`
                : 'Nota fiscal emitida com sucesso.');
        } else {
            mostrarErro(textoRejeicao('Nota rejeitada pela SEFIN', resultado));
        }
    } catch (err) {
        mostrarErroFormulario('erro-emitir-nfse', err.message);
    } finally {
        botaoConfirmar.disabled = false;
    }
}


async function abrirDetalheNfse(id) {
    nfseDetalheAtualId = id;

    try {
        const [nfse, eventos, snapshot] = await Promise.all([
            apiFetch(`/api/nfse/${id}`),
            apiFetch(`/api/nfse/${id}/eventos`),
            apiFetch(`/api/nfse/${id}/snapshot-fiscal`)
        ]);

        document.getElementById('detalhe-nfse-dados').innerHTML = htmlDetalheNfse(nfse, snapshot);
        preencherNomesMunicipios(document.getElementById('detalhe-nfse-dados'));

        document.getElementById('detalhe-nfse-eventos').innerHTML = eventos.length
            ? eventos.map(ev => `
                <li class="list-group-item d-flex justify-content-between">
                    <span>${ev.tipo}${ev.mensagem ? ' — ' + ev.mensagem : ''}</span>
                    <small class="text-muted">${new Date(ev.dataHora).toLocaleString('pt-BR')}</small>
                </li>
            `).join('')
            : '<li class="list-group-item text-muted">Nenhum evento registrado.</li>';

        document.getElementById('detalhe-nfse-snapshot').textContent = snapshot
            ? JSON.stringify(snapshot, null, 2)
            : 'Sem snapshot (nota emitida antes deste recurso existir).';

        // Sempre começa fechado — quem quiser ver o JSON clica em
        // "Mostrar" (ver listener show.bs.collapse/hide.bs.collapse).
        document.getElementById('detalhe-nfse-snapshot-collapse').classList.remove('show');
        const btnToggleSnapshot = document.getElementById('btn-toggle-snapshot');
        btnToggleSnapshot.setAttribute('aria-expanded', 'false');
        btnToggleSnapshot.innerHTML = '<i class="bi bi-chevron-down"></i> Mostrar';

        const btnCancelar = document.getElementById('btn-cancelar-nfse');
        if (btnCancelar) btnCancelar.classList.toggle('d-none', nfse.status !== STATUS_AUTORIZADA);

        // PDF só existe pra nota Autorizada ou Cancelada (mesma regra do
        // backend) — Rascunho/Processando/Rejeitada não têm ChaveAcesso.
        const linkPdf = document.getElementById('link-baixar-danfse');
        const temPdfDisponivel = nfse.status === STATUS_AUTORIZADA || nfse.status === STATUS_CANCELADA;
        linkPdf.href = temPdfDisponivel ? `/api/nfse/${id}/danfse-pdf` : '#';
        linkPdf.classList.toggle('disabled', !temPdfDisponivel);
        linkPdf.setAttribute('aria-disabled', String(!temPdfDisponivel));

        const linkXml = document.getElementById('link-baixar-xml');
        linkXml.href = temPdfDisponivel ? `/api/nfse/${id}/xml` : '#';
        linkXml.classList.toggle('disabled', !temPdfDisponivel);
        linkXml.setAttribute('aria-disabled', String(!temPdfDisponivel));

        modalDetalheNfse.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function confirmarCancelamentoNfse(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-cancelar-nfse');

    const payload = {
        codigoMotivo: parseInt(document.getElementById('codigoMotivoCancelamento').value, 10),
        motivo: document.getElementById('motivoCancelamento').value
    };

    try {
        const resultado = await apiFetch(`/api/nfse/${nfseDetalheAtualId}/cancelar`, { method: 'POST', body: JSON.stringify(payload) });

        modalMotivoCancelamento.hide();
        await carregarNfse();

        if (resultado.sucesso) {
            mostrarToast('Nota fiscal cancelada com sucesso.');
        } else {
            mostrarErro(textoRejeicao('Cancelamento rejeitado pela SEFIN', resultado));
        }
    } catch (err) {
        mostrarErroFormulario('erro-cancelar-nfse', err.message);
    }
}

async function sincronizarComSefin() {
    if (!empresaAtualIdNfse) return;

    const botao = document.getElementById('btn-sincronizar-sefin');
    botao.disabled = true;
    botao.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Buscando...';

    try {
        const resultado = await apiFetch(`/api/empresas/${empresaAtualIdNfse}/sincronizar-sefin`, { method: 'POST' });
        await carregarClientesParaMapa();
        await carregarNfse();

        mostrarToast(
            `Sincronização concluída — importadas: ${resultado.notasImportadas}, já existentes: ${resultado.notasJaExistentes}, ` +
            `canceladas/substituídas atualizadas: ${resultado.notasAtualizadasPorEvento}, ` +
            `ignoradas por conflito: ${resultado.notasIgnoradasPorConflitoNumeracao}, clientes criados: ${resultado.clientesCriados}.`,
            'sucesso'
        );
    } catch (err) {
        mostrarErro(err.message);
    } finally {
        botao.disabled = false;
        botao.innerHTML = '<i class="bi bi-cloud-download"></i> Buscar notas da SEFIN';
    }
}

// ===== Nota Mensal (Fase 7) — emissão em lote =====

function mesAtualIso() {
    const hoje = new Date();
    return `${hoje.getFullYear()}-${String(hoje.getMonth() + 1).padStart(2, '0')}`;
}

async function abrirModalNotaMensal(competenciaMesOverride) {
    if (!empresaAtualIdNfse) {
        mostrarToast('Cadastre a sua empresa em Empresas antes de emitir notas.', 'erro');
        return;
    }
    ocultarErroFormulario('erro-nota-mensal');
    candidatosNotaMensal = [];
    nmNfseIdsEmitidas = [];
    document.getElementById('nm-email-pendentes').classList.add('d-none');
    document.getElementById('nota-mensal-competencia').value = competenciaMesOverride || mesAtualIso();
    document.getElementById('tabela-nota-mensal').classList.add('d-none');
    document.getElementById('nota-mensal-sem-candidatos').classList.add('d-none');
    document.getElementById('linhas-nota-mensal').innerHTML = '';
    atualizarBotaoEmitirNotaMensal();
    modalNotaMensal.show();
    await buscarCandidatosNotaMensal();
}

async function buscarCandidatosNotaMensal() {
    if (!empresaAtualIdNfse) return;
    ocultarErroFormulario('erro-nota-mensal');

    const competenciaMes = document.getElementById('nota-mensal-competencia').value; // yyyy-MM
    if (!competenciaMes) return;
    const competencia = `${competenciaMes}-01`;

    try {
        const candidatos = await apiFetch(`/api/nota-mensal/candidatos?empresaId=${empresaAtualIdNfse}&competencia=${competencia}`);

        candidatosNotaMensal = candidatos.map(c => ({
            ...c,
            selecionado: !c.jaEmitidoNestaCompetencia,
            status: c.jaEmitidoNestaCompetencia ? 'ja-emitido' : 'pendente',
            mensagem: c.jaEmitidoNestaCompetencia ? 'Já tem nota nesta competência.' : null,
            // Um valor por serviço: variável vem com o último emitido, fixo com o do contrato.
            valores: c.linhas.map(l => ({
                contratoServicoId: l.contratoServicoId,
                descricao: l.servicoDescricao,
                variavel: l.valorVariavel === true,
                valorContrato: Number(l.valorTotal),
                ultimoValor: l.ultimoValor != null ? Number(l.ultimoValor) : null,
                ultimaCompetencia: l.ultimaCompetencia,
                valor: Number(l.valorSugerido || l.valorTotal),
                gravar: false
            }))
        }));

        renderizarLinhasNotaMensal();
    } catch (err) {
        mostrarErroFormulario('erro-nota-mensal', err.message);
    }
}

const podeEditarContratosNotaMensal = () =>
    document.getElementById('modal-nota-mensal')?.dataset.podeEditarContratos === 'true';

const reaisNotaMensal = v => Number(v ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const competenciaCurta = iso => iso ? `${iso.substring(5, 7)}/${iso.substring(0, 4)}` : '';

/** Valor fixo diferente do contrato. */
const valorFixoAlterado = s => !s.variavel && Math.round(s.valor * 100) !== Math.round(s.valorContrato * 100);

/** Contrato que não deixa mudar o valor só nesta nota: mudou o fixo, tem que gravar. */
const gravarObrigatorio = (c, s) => valorFixoAlterado(s) && !c.permitirAlterarValorNaEmissao;

function podeEditarServicoNotaMensal(c, s) {
    if (c.status !== 'pendente') return false;
    return s.variavel || c.permitirAlterarValorNaEmissao || podeEditarContratosNotaMensal();
}

/**
 * Dica embaixo do campo: o valor deste serviço na última nota (clique para
 * usar) e, no serviço fixo alterado, "gravar no contrato".
 */
function htmlExtraServicoNotaMensal(c, i, j) {
    const s = c.valores[j];
    let html = '';
    if (s.ultimoValor != null) {
        const igual = Math.round(s.ultimoValor * 100) === Math.round(s.valor * 100);
        html += igual
            ? `<div class="small text-muted"><i class="bi bi-check2"></i> igual à última nota (${competenciaCurta(s.ultimaCompetencia)})</div>`
            : `<div><a href="#" class="small nm-usar-ultimo" data-i="${i}" data-j="${j}" title="Usar o valor deste serviço na última nota">última nota: ${reaisNotaMensal(s.ultimoValor)} (${competenciaCurta(s.ultimaCompetencia)})</a></div>`;
    } else if (s.variavel && c.ultimoTotal == null) {
        html += '<div class="small text-muted">sem nota anterior</div>';
    }
    return html + htmlGravarNoContratoNotaMensal(c, s, i, j);
}

function htmlGravarNoContratoNotaMensal(c, s, i, j) {
    if (!valorFixoAlterado(s)) return '';
    if (!podeEditarContratosNotaMensal())
        return '<span class="small text-muted">só nesta nota</span>';
    const obrigatorio = gravarObrigatorio(c, s);
    const marcado = obrigatorio || s.gravar;
    return `<div class="form-check form-check-inline small mb-0" title="${obrigatorio
            ? 'Este contrato não permite valor diferente só nesta nota: o novo valor vai para o contrato.'
            : 'Marcado, o valor vira o do contrato (Histórico de valores). Desmarcado, vale só nesta nota.'}">
        <input class="form-check-input nm-gravar" type="checkbox" id="nm-gravar-${i}-${j}" data-i="${i}" data-j="${j}"
            ${marcado ? 'checked' : ''} ${obrigatorio || c.status !== 'pendente' ? 'disabled' : ''}>
        <label class="form-check-label" for="nm-gravar-${i}-${j}">gravar no contrato <span class="text-muted">(era ${reaisNotaMensal(s.valorContrato)})</span></label>
    </div>`;
}

function totalCandidatoNotaMensal(c) {
    return c.valores.reduce((soma, s) => soma + (Number(s.valor) || 0), 0);
}

function renderizarLinhasNotaMensal() {
    const corpo = document.getElementById('linhas-nota-mensal');
    const tabela = document.getElementById('tabela-nota-mensal');
    const semCandidatos = document.getElementById('nota-mensal-sem-candidatos');
    const barra = document.getElementById('barra-nota-mensal');

    if (candidatosNotaMensal.length === 0) {
        tabela.classList.add('d-none');
        barra.classList.add('d-none');
        semCandidatos.classList.remove('d-none');
        atualizarBotaoEmitirNotaMensal();
        return;
    }

    tabela.classList.remove('d-none');
    barra.classList.remove('d-none');
    semCandidatos.classList.add('d-none');
    document.getElementById('btn-nm-gravar-todos').classList.toggle('d-none', !podeEditarContratosNotaMensal());

    corpo.innerHTML = candidatosNotaMensal.map((c, i) => {
        const servicos = c.linhas.map(l => escaparHtml(l.servicoDescricao)).join(', ');
        const varios = c.valores.length > 1;
        const campos = c.valores.map((s, j) => `
            <div class="${varios ? 'mb-2' : ''}">
                ${varios ? `<div class="small text-muted text-truncate" title="${escaparHtml(s.descricao)}">${escaparHtml(s.descricao)}${s.variavel
                    ? ' <span class="badge text-bg-light border">variável</span>' : ''}</div>`
                    : (s.variavel ? '<div class="small"><span class="badge text-bg-light border">variável</span></div>' : '')}
                <input type="number" step="0.01" min="0.01" class="form-control form-control-sm nm-valor-servico" data-i="${i}" data-j="${j}"
                    value="${s.valor}" ${podeEditarServicoNotaMensal(c, s) ? '' : 'readonly'} aria-label="Valor de ${escaparHtml(s.descricao)}">
                <div id="nm-extra-${i}-${j}">${htmlExtraServicoNotaMensal(c, i, j)}</div>
            </div>`).join('');

        return `
            <tr>
                <td><input type="checkbox" class="form-check-input nm-check" data-i="${i}" ${c.selecionado ? 'checked' : ''} ${c.status === 'sucesso' ? 'disabled' : ''}></td>
                <td>${escaparHtml(c.clienteNome)}</td>
                <td>${escaparHtml(c.descricao)}<br><small class="text-muted">${servicos}</small>${c.avisoReajuste
                    ? `<div class="small text-warning-emphasis mt-1"><i class="bi bi-exclamation-triangle"></i> ${escaparHtml(c.avisoReajuste)}</div>` : ''}</td>
                <td>
                    ${campos}
                    ${varios ? `<div class="small fw-semibold text-end" id="nm-total-${i}">Total ${reaisNotaMensal(totalCandidatoNotaMensal(c))}</div>` : ''}
                    ${soTotalDaUltimaNota(c)
                        ? `<div class="small text-info-emphasis mt-1" title="Notas emitidas antes de 11/2026 guardam só o total, não o valor de cada serviço">
                            <i class="bi bi-info-circle"></i> Última nota: <strong>${reaisNotaMensal(c.ultimoTotal)}</strong> (${competenciaCurta(c.ultimoTotalCompetencia)}) — divida entre os serviços</div>`
                        : ''}
                </td>
                <td>${renderizarStatusNotaMensal(c)}</td>
            </tr>
        `;
    }).join('');

    corpo.querySelectorAll('.nm-check').forEach(el => el.addEventListener('change', e => {
        candidatosNotaMensal[e.target.dataset.i].selecionado = e.target.checked;
        atualizarTotalNotaMensal();
    }));

    atualizarTotalNotaMensal();
    atualizarBotaoEmitirNotaMensal();
}

/** Campo de valor mudou: atualiza só a dica e o total da linha (sem redesenhar, para não perder o foco). */
function aoMudarValorServicoNotaMensal(i, j, valor) {
    const c = candidatosNotaMensal[i];
    const s = c.valores[j];
    s.valor = parseFloat(valor) || 0;
    if (!valorFixoAlterado(s)) s.gravar = false;
    document.getElementById(`nm-extra-${i}-${j}`).innerHTML = htmlExtraServicoNotaMensal(c, i, j);
    const total = document.getElementById(`nm-total-${i}`);
    if (total) total.textContent = `Total ${reaisNotaMensal(totalCandidatoNotaMensal(c))}`;
    atualizarTotalNotaMensal();
    atualizarBotaoEmitirNotaMensal();
}

function atualizarTotalNotaMensal() {
    const selecionados = candidatosNotaMensal.filter(c => c.selecionado && c.status === 'pendente');
    const total = selecionados.reduce((soma, c) => soma + totalCandidatoNotaMensal(c), 0);
    document.getElementById('total-nota-mensal').textContent = selecionados.length === 0
        ? ''
        : `${selecionados.length} nota(s) selecionada(s) · ${reaisNotaMensal(total)}`;
}

/** Contrato com vários serviços cuja última nota (antiga) só tem o total. */
function soTotalDaUltimaNota(c) {
    return c.valores.length > 1 && c.ultimoTotal != null && c.valores.every(s => s.ultimoValor == null);
}

/**
 * Cada serviço (fixo ou variável) recebe o valor que teve na última nota.
 * Fixo que mudou mostra "gravar no contrato" para você decidir.
 */
function repetirUltimosValoresNotaMensal() {
    let alterados = 0;
    let soTotal = 0;
    candidatosNotaMensal.forEach(c => {
        if (!c.selecionado || c.status !== 'pendente') return;
        if (soTotalDaUltimaNota(c)) {
            soTotal++;
            return;
        }
        c.valores.forEach(s => {
            if (s.ultimoValor != null && Math.round(s.valor * 100) !== Math.round(s.ultimoValor * 100)) {
                s.valor = s.ultimoValor;
                if (!valorFixoAlterado(s)) s.gravar = false;
                alterados++;
            }
        });
    });
    renderizarLinhasNotaMensal();

    const partes = [];
    partes.push(alterados === 0 ? 'Nenhum valor mudou: os campos já estão com os da última nota.' : `${alterados} valor(es) preenchido(s) com o da última nota.`);
    if (soTotal > 0)
        partes.push(`${soTotal} contrato(s) têm só o total da última nota (emitida antes da separação por serviço): o total aparece na linha para você dividir.`);
    mostrarToast(partes.join(' '));
}

function gravarTodosNoContratoNotaMensal() {
    let marcados = 0;
    candidatosNotaMensal.forEach(c => {
        if (!c.selecionado || c.status !== 'pendente') return;
        c.valores.forEach(s => {
            if (valorFixoAlterado(s) && !s.gravar) {
                s.gravar = true;
                marcados++;
            }
        });
    });
    renderizarLinhasNotaMensal();
    mostrarToast(marcados === 0 ? 'Nenhum valor fixo alterado para gravar.' : `${marcados} valor(es) serão gravados nos contratos ao emitir.`);
}

function renderizarStatusNotaMensal(c) {
    if (c.status === 'ja-emitido') return '<span class="badge text-bg-secondary">Já emitido este mês</span>';
    if (c.status === 'sucesso') return `<span class="badge text-bg-success">Emitida${c.numeroNfse ? ' — ' + c.numeroNfse : ''}</span>`;
    if (c.status === 'falha') return `<span class="badge text-bg-danger" title="${c.mensagem ?? ''}">Falhou</span> <small class="text-danger">${c.mensagem ?? ''}</small>`;
    return '<span class="badge text-bg-light text-dark">Pendente</span>';
}

function atualizarBotaoEmitirNotaMensal() {
    const selecionados = candidatosNotaMensal.filter(c => c.selecionado && c.status !== 'sucesso');
    const valoresOk = selecionados.every(c => c.valores.every(s => Number(s.valor) > 0));
    document.getElementById('btn-emitir-nota-mensal').disabled = selecionados.length === 0 || !valoresOk;
}

async function emitirNotaMensal() {
    ocultarErroFormulario('erro-nota-mensal');

    const competenciaMes = document.getElementById('nota-mensal-competencia').value;
    const competencia = `${competenciaMes}-01`;

    const itens = candidatosNotaMensal
        .filter(c => c.selecionado && c.status !== 'sucesso')
        .map(c => ({
            contratoId: c.contratoId,
            valorTotalAjustado: null,
            // Valor de cada serviço; "gravar no contrato" só em fixo alterado.
            servicos: c.valores.map(s => ({
                contratoServicoId: s.contratoServicoId,
                valor: Number(s.valor),
                gravarNoContrato: valorFixoAlterado(s) && (s.gravar || gravarObrigatorio(c, s))
            }))
        }));

    if (itens.length === 0) return;

    const botao = document.getElementById('btn-emitir-nota-mensal');
    botao.disabled = true;

    try {
        const resultados = await apiFetch('/api/nota-mensal/emitir', {
            method: 'POST',
            body: JSON.stringify({ empresaId: empresaAtualIdNfse, competencia, itens })
        });

        // Pode vir mais de 1 resultado pro mesmo contrato (contrato com
        // serviços de códigos de tributação diferentes vira mais de uma
        // nota) — agrega por contratoId: só sucesso se TODAS as notas
        // daquele contrato tiverem saído, mensagens combinadas.
        const porContrato = new Map();
        for (const r of resultados) {
            if (!porContrato.has(r.contratoId)) porContrato.set(r.contratoId, []);
            porContrato.get(r.contratoId).push(r);
        }

        for (const c of candidatosNotaMensal) {
            const doContrato = porContrato.get(c.contratoId);
            if (!doContrato) continue;

            const todasOk = doContrato.every(r => r.sucesso);
            c.status = todasOk ? 'sucesso' : 'falha';
            c.numeroNfse = doContrato.map(r => r.numeroNfse).filter(Boolean).join(', ');
            c.mensagem = todasOk ? null : doContrato.filter(r => !r.sucesso).map(r => r.mensagem).join('; ');
            if (todasOk) c.selecionado = false; // sucesso sai da seleção — reprocessar só pega o que ainda falhou
        }

        // O que foi gravado no contrato já é o valor dele (com ou sem sucesso
        // na SEFIN: a gravação acontece antes de emitir).
        for (const c of candidatosNotaMensal) {
            if (!(porContrato.get(c.contratoId) ?? []).some(r => r.contratoAtualizado)) continue;
            c.valores.forEach(s => {
                if (valorFixoAlterado(s) && (s.gravar || gravarObrigatorio(c, s))) {
                    s.valorContrato = s.valor;
                    s.gravar = false;
                }
            });
        }

        renderizarLinhasNotaMensal();
        await carregarNfse();

        resultados.filter(r => r.sucesso && r.nfseId).forEach(r => {
            if (!nmNfseIdsEmitidas.includes(r.nfseId)) nmNfseIdsEmitidas.push(r.nfseId);
        });
        await atualizarEmailPendentesNotaMensal();

        const totalFalhas = [...porContrato.values()].filter(rs => rs.some(r => !r.sucesso)).length;
        if (totalFalhas === 0) mostrarToast('Lote emitido com sucesso.');
        else mostrarToast(`Lote processado: ${totalFalhas} contrato(s) com falha — revise e emita de novo.`);
    } catch (err) {
        mostrarErroFormulario('erro-nota-mensal', err.message);
    } finally {
        atualizarBotaoEmitirNotaMensal();
    }
}

// Com notas marcadas, a barra de seleção (quantas, total, baixar) toma o
// lugar dos filtros; desmarcou tudo, os filtros voltam.
function atualizarBotaoBaixarSelecionadas() {
    const quantidade = nfseSelecionadas.size;
    document.getElementById('btn-baixar-selecionadas').disabled = quantidade === 0;
    const botaoEmail = document.getElementById('btn-email-selecionadas');
    if (botaoEmail) botaoEmail.disabled = quantidade === 0;
    document.getElementById('barra-selecao-nfse').classList.toggle('d-none', quantidade === 0);
    document.getElementById('filtros-nfse').classList.toggle('d-none', quantidade > 0);
    if (quantidade === 0) return;

    const total = tabelaNfse.rows().data().toArray()
        .filter(n => nfseSelecionadas.has(n.id))
        .reduce((soma, n) => soma + Number(n.valorServico || 0), 0);
    document.getElementById('selecao-nfse-resumo').textContent =
        `${quantidade} ${quantidade === 1 ? 'nota selecionada' : 'notas selecionadas'} · ${formatarMoedaNfse(total)}`;
}

async function baixarDanfsePdfLote() {
    if (nfseSelecionadas.size === 0) return;

    const botao = document.getElementById('btn-baixar-selecionadas');
    botao.disabled = true;
    const iconeOriginal = botao.innerHTML;
    botao.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Gerando...';

    try {
        // application/zip não é JSON — usa fetch direto, não apiFetch
        // (que sempre tenta .json() na resposta). Mesmo padrão já usado
        // pra upload de arquivo em outras telas.
        const resposta = await fetch('/api/nfse/danfse-pdf/lote', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({ nfseIds: Array.from(nfseSelecionadas) })
        });

        if (!resposta.ok) {
            const corpo = await resposta.json().catch(() => null);
            throw new Error(extrairMensagemDeErro(corpo) ?? `Erro ${resposta.status} ao gerar o lote de PDFs.`);
        }

        const idsIgnorados = resposta.headers.get('X-Nfse-Ids-Ignorados');
        const blob = await resposta.blob();

        // Nome do arquivo vem no Content-Disposition — o navegador não
        // expõe isso pro JS automaticamente, então repete a mesma
        // convenção (data/hora) aqui só pro nome do .zip local.
        const nomeZip = `DANFSe-lote-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.zip`;
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = nomeZip;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);

        if (idsIgnorados) {
            const total = idsIgnorados.split(',').length;
            mostrarToast(`Zip gerado. ${total} nota(s) ficaram de fora por não ter DANFSe disponível (precisa estar Autorizada ou Cancelada).`, 'erro');
        } else {
            mostrarToast('Zip com os PDFs selecionados gerado.');
        }
    } catch (err) {
        mostrarErro(err.message);
    } finally {
        botao.disabled = nfseSelecionadas.size === 0;
        botao.innerHTML = iconeOriginal;
    }
}

// ---------------------------------------------------------------------
// E-mail da nota ao cliente: uma nota (modal com o texto do modelo já
// preenchido, ajustável só neste envio), várias (barra de seleção) e o
// atalho da Nota Mensal. Entra na fila de e-mails (PDF e XML anexados na
// hora do envio); o resultado final aparece na tela de E-mails.
// ---------------------------------------------------------------------

// StatusEmailLog (Domain.Enums): 0 Pendente, 1 Enviado, 2 Falhou.
function envelopeEmailNfse(n) {
    if (n.emailUltimoStatus == null) return '';
    const quando = n.emailUltimoEnvioEm ? new Date(n.emailUltimoEnvioEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : '';
    const [icone, cor, texto] = n.emailUltimoStatus === 1 ? ['bi-envelope-check', 'text-success', `E-mail enviado em ${quando}`]
        : n.emailUltimoStatus === 2 ? ['bi-envelope-exclamation', 'text-danger', 'O último e-mail falhou — veja na tela de E-mails']
        : ['bi-envelope', 'text-muted', 'E-mail na fila de envio'];
    return ` <i class="bi ${icone} ${cor} ms-1" title="${escaparHtml(texto)}" aria-label="${escaparHtml(texto)}"></i>`;
}

function configurarEmailNfse() {
    const modalEnvio = document.getElementById('modal-email-nfse');
    if (!modalEnvio) return; // sem permissão de emitir
    modalEmailNfse = new bootstrap.Modal(modalEnvio);
    modalEmailLote = new bootstrap.Modal(document.getElementById('modal-email-lote'));

    document.getElementById('email-nfse-modelo').addEventListener('change', trocarModeloEmailNfse);
    document.getElementById('form-email-nfse').addEventListener('submit', async e => {
        e.preventDefault();
        await executarComBotaoDesabilitado(document.getElementById('btn-confirmar-email-nfse'), enviarEmailNfse);
    });
    document.getElementById('btn-email-selecionadas').addEventListener('click', () => abrirModalEmailLote([...nfseSelecionadas]));
    document.getElementById('btn-confirmar-email-lote').addEventListener('click', e =>
        executarComBotaoDesabilitado(e.currentTarget, enviarEmailLote));
    document.getElementById('btn-nm-enviar-email')?.addEventListener('click', e =>
        executarComBotaoDesabilitado(e.currentTarget, enviarEmailPendentesNotaMensal));
}

async function abrirModalEmailNfse(id) {
    ocultarErroFormulario('erro-email-nfse');
    let previa;
    try {
        previa = await apiFetch(`/api/nfse/${id}/email-previa`);
    } catch (err) {
        mostrarErro(err.message);
        return;
    }
    const seletor = document.getElementById('email-nfse-modelo');
    seletor.innerHTML = (previa.modelos ?? []).map(m =>
        `<option value="${m.id}">${escaparHtml(m.nome)}${m.padrao ? ' (padrão)' : ''}</option>`).join('') ||
        '<option value="">Texto padrão do sistema</option>';
    seletor.value = previa.modeloId ?? '';
    if (previa.impedimento) {
        mostrarErro(previa.impedimento === 'Cliente sem e-mail no cadastro.'
            ? 'O cliente desta nota não tem e-mail cadastrado. Inclua o e-mail no cadastro do cliente e tente de novo.'
            : previa.impedimento);
        return;
    }

    nfseIdEmailAtual = id;
    mostrarPreviaEmailNfse(previa);
    modalEmailNfse.show();
}

// Trocou o modelo no modal: texto, respostas e cópia oculta vêm do novo.
async function trocarModeloEmailNfse() {
    const modeloId = document.getElementById('email-nfse-modelo').value;
    try {
        const previa = await apiFetch(`/api/nfse/${nfseIdEmailAtual}/email-previa${modeloId ? `?modeloId=${modeloId}` : ''}`);
        mostrarPreviaEmailNfse(previa);
    } catch (err) {
        mostrarErroFormulario('erro-email-nfse', err.message);
    }
}

function mostrarPreviaEmailNfse(previa) {
    const linha = (rotulo, valor) => `<dt class="col-sm-3 text-muted fw-normal">${rotulo}</dt><dd class="col-sm-9 mb-1">${valor}</dd>`;
    const copias = previa.copias?.length
        ? linha(previa.copiasOcultas ? 'Cópia oculta' : 'Cópia', previa.copias.map(escaparHtml).join(', ')) : '';
    const fixa = previa.copiaOcultaPara ? linha('Cópia oculta', escaparHtml(previa.copiaOcultaPara)) : '';
    const resposta = previa.responderPara ? linha('Respostas para', escaparHtml(previa.responderPara)) : '';
    document.getElementById('email-nfse-destino').innerHTML =
        linha('Para', `<strong>${escaparHtml(previa.destinatario)}</strong>`) + copias + fixa + resposta;

    const ultimo = document.getElementById('email-nfse-ultimo');
    ultimo.innerHTML = previa.ultimoEnvioEm
        ? `<i class="bi bi-info-circle me-1"></i>${previa.ultimoStatus === 2 ? 'O último envio falhou' : 'Já enviada'} em ` +
          `${new Date(previa.ultimoEnvioEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}. Enviar de novo manda outra cópia.`
        : '';
    document.getElementById('email-nfse-homologacao').classList.toggle('d-none', !previa.homologacao);
    document.getElementById('email-nfse-assunto').value = previa.assunto;
    document.getElementById('email-nfse-mensagem').value = previa.mensagem;
    mostrarAnexosEmailNfse(previa.documentos || []);
}

// ===== DOC2: documentos exigidos pelo cliente no modal de envio =====
// SituacaoDocumentoExigido: 1 em dia, 2 vencido, 3 faltando.
const LIMITE_AVISO_ANEXOS = 10 * 1024 * 1024;
let confirmouVencidosEmail = false;

function tamanhoAnexo(bytes) {
    if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1).replace('.', ',') + ' MB';
    return Math.max(1, Math.round(bytes / 1024)) + ' KB';
}

function mostrarAnexosEmailNfse(documentos) {
    confirmouVencidosEmail = false;
    document.getElementById('btn-confirmar-email-nfse').innerHTML = '<i class="bi bi-send"></i> Enviar';
    document.getElementById('email-nfse-aviso-vencido').classList.add('d-none');
    const bloco = document.getElementById('email-nfse-anexos');
    bloco.classList.toggle('d-none', documentos.length === 0);

    const fixos = '<li class="list-group-item d-flex align-items-center gap-2 text-muted"><input class="form-check-input mt-0" type="checkbox" checked disabled aria-label="PDF e XML da nota" />' +
        '<i class="bi bi-receipt" aria-hidden="true"></i> Nota (PDF e XML)</li>';
    document.getElementById('email-nfse-anexos-lista').innerHTML = fixos + documentos.map(d => {
        const cor = d.situacao === 1 ? 'success' : d.situacao === 2 ? 'danger' : 'secondary';
        const id = d.id ? `anexo-doc-${d.id}` : '';
        const caixa = d.id
            ? `<input class="form-check-input mt-0" type="checkbox" id="${id}" data-doc-id="${d.id}" data-vencido="${d.situacao === 2}" data-tamanho="${d.tamanhoBytes}" checked />`
            : '<input class="form-check-input mt-0" type="checkbox" disabled aria-label="Documento faltando" />';
        const extra = d.situacao === 3
            ? ' · <a href="/Documentos" target="_blank" rel="noopener">enviar em Documentos da empresa</a>'
            : ` · ${tamanhoAnexo(d.tamanhoBytes)}`;
        return `<li class="list-group-item d-flex flex-wrap align-items-center gap-2">
            ${caixa}
            <label class="me-auto mb-0" ${id ? `for="${id}"` : ''}>${escaparHtml(d.titulo)}</label>
            <span class="small text-${cor}">${escaparHtml(d.situacaoTexto)}${extra}</span>
        </li>`;
    }).join('');

    document.querySelectorAll('#email-nfse-anexos-lista [data-doc-id]').forEach(c => c.addEventListener('change', () => {
        confirmouVencidosEmail = false;
        document.getElementById('email-nfse-aviso-vencido').classList.add('d-none');
        document.getElementById('btn-confirmar-email-nfse').innerHTML = '<i class="bi bi-send"></i> Enviar';
        atualizarTamanhoAnexosEmail();
    }));
    atualizarTamanhoAnexosEmail();
}

function atualizarTamanhoAnexosEmail() {
    const total = [...document.querySelectorAll('#email-nfse-anexos-lista [data-doc-id]:checked')]
        .reduce((soma, c) => soma + Number(c.dataset.tamanho || 0), 0);
    const aviso = document.getElementById('email-nfse-aviso-tamanho');
    aviso.classList.toggle('d-none', total <= LIMITE_AVISO_ANEXOS);
    aviso.textContent = `Os documentos somam ${tamanhoAnexo(total)}. E-mails acima de 10 MB podem ser recusados pelo provedor do cliente; se puder, desmarque algum.`;
}

/** Ids marcados; null quando a nota não tem documento exigido. */
function documentosMarcadosEmail() {
    if (document.getElementById('email-nfse-anexos').classList.contains('d-none')) return null;
    return [...document.querySelectorAll('#email-nfse-anexos-lista [data-doc-id]:checked')].map(c => c.dataset.docId);
}

/** Vencido marcado: pergunta "mandar assim mesmo?" dentro do modal (segundo clique confirma). */
function precisaConfirmarVencidosEmail() {
    const vencidos = [...document.querySelectorAll('#email-nfse-anexos-lista [data-doc-id][data-vencido="true"]:checked')];
    if (vencidos.length === 0 || confirmouVencidosEmail) return false;
    const nomes = vencidos.map(c => document.querySelector(`label[for="${c.id}"]`)?.textContent).join(', ');
    const aviso = document.getElementById('email-nfse-aviso-vencido');
    aviso.innerHTML = `<i class="bi bi-exclamation-triangle" aria-hidden="true"></i> ${vencidos.length === 1 ? 'Documento vencido' : 'Documentos vencidos'}: <strong>${escaparHtml(nomes)}</strong>. ` +
        'Mandar assim mesmo? Clique de novo para confirmar, ou desmarque o documento.';
    aviso.classList.remove('d-none');
    confirmouVencidosEmail = true;
    document.getElementById('btn-confirmar-email-nfse').innerHTML = '<i class="bi bi-send"></i> Mandar assim mesmo';
    return true;
}

async function enviarEmailNfse() {
    ocultarErroFormulario('erro-email-nfse');
    const assunto = document.getElementById('email-nfse-assunto').value.trim();
    const mensagem = document.getElementById('email-nfse-mensagem').value.trim();
    if (!assunto || !mensagem) {
        mostrarErroFormulario('erro-email-nfse', 'Preencha o assunto e a mensagem.');
        return;
    }
    if (precisaConfirmarVencidosEmail()) return;
    try {
        const resultado = await apiFetch(`/api/nfse/${nfseIdEmailAtual}/enviar-email`, {
            method: 'POST',
            body: JSON.stringify({
                assunto, mensagem, modeloId: document.getElementById('email-nfse-modelo').value || null,
                documentosIds: documentosMarcadosEmail(), enviarVencidos: confirmouVencidosEmail
            })
        });
        modalEmailNfse.hide();
        const copias = resultado.copias?.length ? ` (+${resultado.copias.length} em cópia)` : '';
        mostrarToast(`Nota enviada para ${resultado.destinatario}${copias}. Acompanhe a entrega na tela de E-mails.`);
        if (resultado.alertaDocumentos) mostrarToast(`Atenção aos documentos: ${resultado.alertaDocumentos}`, 'erro');
        await carregarNfse();
    } catch (err) {
        mostrarErroFormulario('erro-email-nfse', err.message);
    }
}

function plural(n, um, varios) {
    return `${n} ${n === 1 ? um : varios}`;
}

async function abrirModalEmailLote(ids) {
    if (!ids.length) return;
    idsEmailLote = ids;
    ocultarErroFormulario('erro-email-lote');
    const resultado = document.getElementById('email-lote-resultado');
    resultado.classList.add('d-none');
    resultado.innerHTML = '';
    document.getElementById('btn-confirmar-email-lote').classList.remove('d-none');
    document.getElementById('btn-fechar-email-lote').textContent = 'Cancelar';
    document.getElementById('email-lote-pular').checked = true;

    let previa;
    try {
        previa = await apiFetch('/api/nfse/enviar-email/lote/previa', { method: 'POST', body: JSON.stringify({ nfseIds: ids }) });
    } catch (err) {
        mostrarErro(err.message);
        return;
    }

    const fora = [];
    if (previa.semEmail) fora.push(`${plural(previa.semEmail, 'sem e-mail no cadastro do cliente', 'sem e-mail no cadastro do cliente')}`);
    if (previa.naoAutorizadas) fora.push(`${plural(previa.naoAutorizadas, 'não autorizada', 'não autorizadas')}`);
    document.getElementById('email-lote-resumo').innerHTML =
        `<p class="mb-2"><strong>${plural(previa.enviaveis, 'nota', 'notas')}</strong> para <strong>${plural(previa.clientes, 'cliente', 'clientes')}</strong>.</p>` +
        (previa.jaEnviadas ? `<p class="mb-2 small">${plural(previa.jaEnviadas, 'já foi enviada', 'já foram enviadas')} antes.</p>` : '') +
        (fora.length ? `<p class="mb-0 small text-muted">Não vão: ${fora.join(', ')}.</p>` : '');
    document.getElementById('email-lote-opcao-pular').classList.toggle('d-none', previa.jaEnviadas === 0);
    mostrarDocumentosEmailLote(previa);
    document.getElementById('btn-confirmar-email-lote').disabled = previa.enviaveis === 0;
    modalEmailLote.show();
}

// DOC2: aviso do lote quando cliente exige documento vencido ou faltando.
function mostrarDocumentosEmailLote(previa) {
    const vencidos = previa.comDocumentoVencido || 0;
    const faltando = previa.comDocumentoFaltando || 0;
    const caixa = document.getElementById('email-lote-documentos');
    caixa.classList.toggle('d-none', vencidos === 0 && faltando === 0);
    document.getElementById('email-lote-opcao-vencidos').classList.toggle('d-none', vencidos === 0);
    document.getElementById('email-lote-vencidos').checked = false;
    const partes = [];
    if (vencidos) partes.push(`${plural(vencidos, 'nota tem', 'notas têm')} documento exigido pelo cliente <strong>vencido</strong> (sem marcar abaixo, vai sem ele)`);
    if (faltando) partes.push(`${plural(faltando, 'nota tem', 'notas têm')} documento exigido que ainda não foi enviado em <a href="/Documentos">Documentos da empresa</a> (vai sem ele)`);
    document.getElementById('email-lote-documentos-texto').innerHTML =
        '<i class="bi bi-exclamation-triangle" aria-hidden="true"></i> ' + partes.join('; ') + '. O aviso fica registrado em cada e-mail.';
}

async function enviarEmailLote() {
    ocultarErroFormulario('erro-email-lote');
    try {
        const r = await apiFetch('/api/nfse/enviar-email/lote', {
            method: 'POST',
            body: JSON.stringify({
                nfseIds: idsEmailLote, pularJaEnviadas: document.getElementById('email-lote-pular').checked,
                enviarDocumentosVencidos: document.getElementById('email-lote-vencidos').checked
            })
        });
        const resultado = document.getElementById('email-lote-resultado');
        const falhas = (r.naoEnviadas ?? []).map(n =>
            `<li>Nota ${escaparHtml(n.numero)} — ${escaparHtml(n.cliente)}: ${escaparHtml(n.motivo)}</li>`).join('');
        resultado.innerHTML =
            `<div class="alert alert-success mb-2"><i class="bi bi-check2-circle me-1"></i>${plural(r.enfileiradas, 'e-mail na fila de envio', 'e-mails na fila de envio')}` +
            `${r.puladas ? ` · ${plural(r.puladas, 'pulada (já enviada)', 'puladas (já enviadas)')}` : ''}.</div>` +
            (falhas ? `<div class="small">Não enviadas:<ul class="mb-0">${falhas}</ul></div>` : '');
        resultado.classList.remove('d-none');
        document.getElementById('btn-confirmar-email-lote').classList.add('d-none');
        document.getElementById('btn-fechar-email-lote').textContent = 'Fechar';
        await carregarNfse();
    } catch (err) {
        mostrarErroFormulario('erro-email-lote', err.message);
    }
}

// Nota Mensal: depois de emitir, quantas notas ainda não foram por e-mail
// (o envio automático só sai para cliente com "enviar por e-mail" ligado).
async function atualizarEmailPendentesNotaMensal() {
    const caixa = document.getElementById('nm-email-pendentes');
    if (!caixa || nmNfseIdsEmitidas.length === 0) return;
    try {
        const previa = await apiFetch('/api/nfse/enviar-email/lote/previa', { method: 'POST', body: JSON.stringify({ nfseIds: nmNfseIdsEmitidas }) });
        const pendentes = previa.enviaveis - previa.jaEnviadas;
        caixa.classList.toggle('d-none', pendentes <= 0 && previa.semEmail === 0);
        document.getElementById('btn-nm-enviar-email').classList.toggle('d-none', pendentes <= 0);
        document.getElementById('nm-email-pendentes-texto').textContent = pendentes > 0
            ? `${plural(pendentes, 'nota emitida ainda não foi', 'notas emitidas ainda não foram')} por e-mail ao cliente` +
              (previa.semEmail ? ` (${previa.semEmail} sem e-mail no cadastro).` : '.')
            : `${plural(previa.semEmail, 'cliente', 'clientes')} sem e-mail no cadastro: a nota não foi por e-mail.`;
    } catch {
        caixa.classList.add('d-none');
    }
}

async function enviarEmailPendentesNotaMensal() {
    try {
        const r = await apiFetch('/api/nfse/enviar-email/lote', {
            method: 'POST', body: JSON.stringify({ nfseIds: nmNfseIdsEmitidas, pularJaEnviadas: true })
        });
        mostrarToast(`${plural(r.enfileiradas, 'e-mail na fila de envio', 'e-mails na fila de envio')}.`);
        await atualizarEmailPendentesNotaMensal();
        await carregarNfse();
    } catch (err) {
        mostrarErroFormulario('erro-nota-mensal', err.message);
    }
}

/**
 * Rejeição da SEFIN em texto (toast): código, mensagem oficial e, quando o
 * código está no catálogo (CatalogoRejeicoesSefin), o que fazer.
 */
function textoRejeicao(prefixo, resultado) {
    const codigo = resultado.codigoErro ? ` (${resultado.codigoErro})` : '';
    const mensagem = resultado.mensagemErro ?? resultado.rejeicao?.mensagemOficial ?? 'motivo não informado';
    const oQueFazer = resultado.rejeicao ? ` O que fazer: ${resultado.rejeicao.oQueFazer}` : '';
    return `${prefixo}${codigo}: ${mensagem}.${oQueFazer}`;
}

/**
 * Campo "Erro" do detalhe da nota: mensagem oficial e, se houver, a
 * explicação (onde corrigir, o que fazer e o atalho para a tela). Tudo que
 * vem da SEFIN passa por escaparHtml.
 */
function htmlErroNfse(nfse) {
    const rejeicao = nfse.rejeicao;
    const mensagem = nfse.mensagemErro ?? rejeicao?.mensagemOficial;
    if (!mensagem && !nfse.codigoErro) return '—';

    const base = `${escaparHtml(nfse.codigoErro ?? '')} — ${escaparHtml(mensagem ?? 'motivo não informado')}`;
    if (!rejeicao) return base;

    const link = rejeicao.linkTela
        ? ` <a href="${escaparHtml(rejeicao.linkTela)}">Abrir ${escaparHtml(rejeicao.ondeCorrigir.toLowerCase())}</a>`
        : '';
    return `${base}
        <div class="rejeicao-explicada">
            <div><strong>Onde corrigir:</strong> ${escaparHtml(rejeicao.ondeCorrigir)}</div>
            <div><strong>O que fazer:</strong> ${escaparHtml(rejeicao.oQueFazer)}${link}</div>
        </div>`;
}

/**
 * Corpo do detalhe da nota, em blocos. "Descrição na nota" é o texto que
 * foi de fato para a SEFIN (Nfse.DescricaoServico) — não confundir com o
 * nome do serviço no catálogo, que está no snapshot (descricaoCadastro).
 * Tudo que vem de cadastro ou da SEFIN passa por escaparHtml.
 */
function htmlDetalheNfse(nfse, snapshot) {
    const rotulo = ROTULOS_STATUS[nfse.status] ?? { texto: 'Desconhecido', cor: 'secondary' };
    const moeda = v => v == null ? '—' : Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    const linha = (titulo, valorHtml) => `<dt class="col-sm-4">${titulo}</dt><dd class="col-sm-8">${valorHtml}</dd>`;

    const competencia = nfse.dataCompetencia
        ? `${nfse.dataCompetencia.slice(5, 7)}/${nfse.dataCompetencia.slice(0, 4)}`
        : '—';
    const emissao = nfse.dataEmissao ? new Date(nfse.dataEmissao).toLocaleString('pt-BR') : '—';
    const ambiente = nfse.tipoAmbiente === 1 ? 'Produção' : 'Homologação (teste, sem valor fiscal)';

    const cliente = snapshot?.cliente
        ? `${snapshot.cliente.nome} (${snapshot.cliente.cpfCnpj})`
        : (clientesPorId[nfse.clienteId]?.nome ?? clientesPorId[nfse.clienteId]?.apelido ?? '—');
    const servicoCatalogo = snapshot?.servico?.descricaoCadastro ?? '—';

    const iss = nfse.issRetido
        ? `Retido pelo tomador${nfse.aliquotaIss != null ? ` — alíquota ${Number(nfse.aliquotaIss).toLocaleString('pt-BR')}%` : ''}`
        : 'Não retido';

    const temErro = nfse.mensagemErro || nfse.codigoErro;

    return `
        <section class="detalhe-bloco">
            <h6>Nota</h6>
            <dl class="row mb-0">
                ${linha('Situação', `<span class="badge text-bg-${rotulo.cor}">${rotulo.texto}</span>`)}
                ${linha('Número da NFS-e', escaparHtml(nfse.numeroNfse ?? '—'))}
                ${linha('DPS (número/série)', `${nfse.numeroDps}/${escaparHtml(nfse.serieDps)}`)}
                ${linha('Ambiente', ambiente)}
                ${linha('Competência', competencia)}
                ${linha('Emissão', emissao)}
                ${linha('Chave de acesso', `<span class="text-break">${escaparHtml(nfse.chaveAcesso ?? '—')}</span>`)}
                ${nfse.reenvioDeNumeroDps != null ? linha('Reenvio de', `DPS nº ${nfse.reenvioDeNumeroDps} (rejeitada)`) : ''}
                ${nfse.reenviadaComoNumeroDps != null ? linha('Reenviada como', `DPS nº ${nfse.reenviadaComoNumeroDps}`) : ''}
                ${nfse.substituiNumeroDps != null ? linha('Substitui', `DPS nº ${nfse.substituiNumeroDps}${nfse.descricaoMotivoSubstituicao ? ' — ' + escaparHtml(nfse.descricaoMotivoSubstituicao) : ''}`) : ''}
                ${nfse.substituidaPorNumeroDps != null ? linha('Substituída por', `DPS nº ${nfse.substituidaPorNumeroDps}`) : ''}
            </dl>
        </section>

        <section class="detalhe-bloco">
            <h6>Cliente e serviço</h6>
            <dl class="row mb-0">
                ${linha('Cliente', escaparHtml(cliente))}
                ${linha('Serviço (catálogo)', escaparHtml(servicoCatalogo))}
                ${linha('Cód. tributação nacional', escaparHtml(nfse.codigoTributacaoNacional ?? '—'))}
                ${linha('NBS', escaparHtml(nfse.codigoNbs ?? '—'))}
            </dl>
        </section>

        <section class="detalhe-bloco">
            <h6>Descrição na nota</h6>
            <div class="descricao-nota">${escaparHtml(nfse.descricaoServico)}</div>
            <div class="form-text">Texto enviado à SEFIN — é o que aparece em "Descrição do serviço" no PDF e no portal da NFS-e.</div>
        </section>

        <section class="detalhe-bloco">
            <h6>Valores e tributação</h6>
            <dl class="row mb-0">
                ${linha('Valor do serviço', `<strong>${moeda(nfse.valorServico)}</strong>`)}
                ${linha('Valor líquido', moeda(nfse.valorLiquido))}
                ${(nfse.composicao ?? []).length > 1
                    ? linha('Composição', nfse.composicao.map(i => `${escaparHtml(i.descricao)}: ${Number(i.valor).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}`).join('<br>'))
                    : ''}
                ${linha('ISS', iss)}
                ${linhasDetalheRetencoes(nfse.descontosRetencoes, linha, moeda)}
                ${linha('Local da prestação', nfse.codigoMunicipioPrestacao
                    ? `<span data-municipio-codigo="${escaparHtml(nfse.codigoMunicipioPrestacao)}">${escaparHtml(nfse.codigoMunicipioPrestacao)}</span>`
                    : 'Município da empresa')}
            </dl>
        </section>

        ${temErro ? `
        <section class="detalhe-bloco">
            <h6>Erro</h6>
            ${htmlErroNfse(nfse)}
        </section>` : ''}`;
}

// ---------- XML do mês (para o contador) ----------

function abrirXmlMes() {
    if (!empresaAtualIdNfse) return;
    ocultarErroFormulario('erro-xml-mes');
    // Padrão: o mês anterior (o que o contador pede no começo do mês).
    const hoje = new Date();
    const anterior = new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1);
    const campo = document.getElementById('xml-mes-competencia');
    if (!campo.value)
        campo.value = `${anterior.getFullYear()}-${String(anterior.getMonth() + 1).padStart(2, '0')}`;
    bootstrap.Modal.getOrCreateInstance(document.getElementById('modal-xml-mes')).show();
}

async function baixarXmlMes(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-xml-mes');
    const mes = document.getElementById('xml-mes-competencia').value; // yyyy-MM
    if (!mes) {
        mostrarErroFormulario('erro-xml-mes', 'Escolha o mês.');
        return;
    }

    const botao = document.getElementById('btn-baixar-xml-mes');
    await executarComBotaoDesabilitado(botao, async function () {
        try {
            // application/zip não é JSON: fetch direto (mesmo padrão do lote de PDFs).
            const resposta = await fetch(`/api/nfse/xml-mes?empresaId=${encodeURIComponent(empresaAtualIdNfse)}&competencia=${mes}-01`, {
                credentials: 'same-origin'
            });
            if (!resposta.ok) {
                const corpo = await resposta.json().catch(() => null);
                throw new Error(extrairMensagemDeErro(corpo) ?? `Erro ${resposta.status} ao gerar o arquivo.`);
            }

            const quantidade = resposta.headers.get('X-Quantidade-Notas');
            const nome = (resposta.headers.get('Content-Disposition') || '').match(/filename="?([^";]+)"?/)?.[1] || `XML-NFSe-${mes}.zip`;
            const blob = await resposta.blob();
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = nome;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);

            bootstrap.Modal.getInstance(document.getElementById('modal-xml-mes'))?.hide();
            mostrarToast(`Arquivo com ${quantidade ?? 'os'} XML(s) baixado.`);
        } catch (err) {
            mostrarErroFormulario('erro-xml-mes', err.message);
        }
    });
}

// C2: troca o código IBGE pelo "Nome/UF (código)" nos elementos marcados.
async function preencherNomesMunicipios(raiz) {
    for (const el of raiz.querySelectorAll('[data-municipio-codigo]')) {
        try {
            const m = await apiFetch(`/api/consultas/municipio/${el.dataset.municipioCodigo}`);
            el.textContent = `${m.nome}${m.uf ? '/' + m.uf : ''} (${el.dataset.municipioCodigo})`;
        } catch {
            // Fica o código.
        }
    }
}

// ---------- Substituição de nota autorizada (C7) ----------

// Mesmo modal da emissão, pré-preenchido com a nota original. Cliente,
// contrato e competência ficam travados; valor, serviço e local conforme o
// regime da empresa (a SEFIN não deixa mudar — ver RegrasSubstituicao).
async function abrirModalSubstituicaoNfse(id) {
    try {
        const [nfse, dados] = await Promise.all([
            apiFetch(`/api/nfse/${id}`),
            apiFetch(`/api/nfse/${id}/substituicao`)
        ]);
        if (dados.impedimento) {
            mostrarErro(dados.impedimento);
            return;
        }
        await prepararModalEmitirNfse();
        nfseSubstituicaoAtual = { id: nfse.id, numeroDps: nfse.numeroDps };
        document.getElementById('titulo-modal-emitir-nfse').textContent =
            `Substituir nota${nfse.numeroNfse ? ' nº ' + nfse.numeroNfse : ''} — DPS nº ${nfse.numeroDps}`;
        mostrarCamposEmissao();

        const aviso = document.getElementById('aviso-reenvio-nfse');
        aviso.replaceChildren();
        const titulo = document.createElement('strong');
        titulo.textContent = 'Substituição: ';
        const texto = document.createElement('span');
        texto.textContent = 'corrija o que precisar e confirme. Autorizada a nova nota, a SEFIN cancela a original automaticamente.';
        const dica = document.createElement('div');
        dica.className = 'small mt-1';
        dica.textContent = `Pelo regime da empresa, não podem mudar: ${dados.textoCamposTravados}.`;
        aviso.append(titulo, texto, dica);
        aviso.classList.remove('d-none');

        const selectMotivo = document.getElementById('emitir-motivoSubstituicao');
        selectMotivo.replaceChildren(...dados.motivos.map(m => new Option(`${m.codigo} - ${m.descricao}`, m.codigo)));
        document.getElementById('emitir-descricaoMotivoSubstituicao').value = '';
        document.getElementById('bloco-substituicao-nfse').classList.remove('d-none');

        const selectCliente = document.getElementById('emitir-clienteId');
        selectCliente.append(new Option(clientesPorId[nfse.clienteId]?.apelido ?? nfse.clienteId, nfse.clienteId, true, true));
        $('#emitir-clienteId').prop('disabled', true).trigger('change');

        const campoContrato = document.getElementById('campo-emitir-contrato');
        const selectContrato = document.getElementById('emitir-contratoId');
        if (nfse.contratoId) {
            selectContrato.innerHTML = `<option value="${nfse.contratoId}">Mesmo contrato da nota original</option>`;
            selectContrato.disabled = true;
            campoContrato.classList.remove('d-none');
        }

        const selectServico = document.getElementById('emitir-servicoId');
        if (nfse.servicoId && [...selectServico.options].some(o => o.value === nfse.servicoId)) {
            selectServico.value = nfse.servicoId;
        } else {
            selectServico.insertBefore(new Option('Selecione o serviço…', '', true, true), selectServico.firstChild);
        }

        document.getElementById('emitir-descricaoServico').value = nfse.descricaoServico;
        document.getElementById('emitir-valorServico').value = nfse.valorServico;
        sugestaoEmissao = { descricao: nfse.descricaoServico, valor: String(nfse.valorServico) };
        descricaoEditadaPeloUsuario = true;
        valorEditadoPeloUsuario = true;
        atualizarAvisoDescricaoEmissao();
        document.getElementById('emitir-dataCompetencia').value = nfse.dataCompetencia;
        definirIssRetido(nfse.issRetido === true);
        if (nfse.issRetido && nfse.aliquotaIss != null) document.getElementById('emitir-aliquotaIss').value = nfse.aliquotaIss;
        // C6: descontos e retenções da original (percentual = valor retido / base).
        definirDescontosNaTela(nfse.descontosRetencoes);
        definirRetencoesNaTela(percentuaisDaNota(nfse));
        if (temDescontoOuRetencao(nfse.descontosRetencoes)) abrirBlocoRetencoes(true);
        await atualizarRetencoesEmissao();
        await definirMunicipioNoSelect('emitir-localPrestacao', nfse.codigoMunicipioPrestacao);

        // CampoTravadoSubstituicao: 1 Cliente, 2 Competência, 3 Valor, 4 Serviço, 5 Local.
        // Competência fica travada sempre (os dois regimes).
        document.getElementById('emitir-dataCompetencia').disabled = true;
        if (dados.camposTravados.includes(3)) document.getElementById('emitir-valorServico').disabled = true;
        if (dados.camposTravados.includes(4)) selectServico.disabled = true;
        if (dados.camposTravados.includes(5)) $('#emitir-localPrestacao').prop('disabled', true);

        modalEmitirNfse.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}
