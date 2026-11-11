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
                render: (d, t, n) => t !== 'display'
                    ? `${n.numeroNfse ?? ''} ${n.numeroDps}`
                    : n.numeroNfse
                        ? `<strong>${escaparHtml(n.numeroNfse)}</strong> <br><small class="text-muted">DPS ${n.numeroDps}/${escaparHtml(n.serieDps)}</small>`
                        : `<span class="text-muted">DPS ${n.numeroDps}/${escaparHtml(n.serieDps)}</span>`
            },
            {
                // Apelido na grade; razão social na dica (title) do mouse.
                data: 'clienteId',
                render: id => {
                    const c = clientesPorId[id];
                    return c ? `<span title="${escaparHtml(c.nome)}">${escaparHtml(c.apelido)}</span>` : id;
                }
            },
            { data: 'valorServico', render: v => Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
            { data: 'valorLiquido', render: v => v == null ? '—' : Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
            {
                data: 'status',
                render: (v, t, n) => {
                    const r = ROTULOS_STATUS[v] ?? { texto: 'Desconhecido', cor: 'secondary' };
                    const reenviada = n.reenviadaComoNumeroDps != null
                        ? `<br><small class="text-muted">Reenviada → nº ${n.reenviadaComoNumeroDps}</small>`
                        : '';
                    return `<span class="badge text-bg-${r.cor}">${r.texto}</span>${reenviada}`;
                }
            },
            {
                data: 'dataEmissao',
                render: {
                    // Formato "DD/MM/AAAA, HH:mm:ss" não é ordenável como
                    // texto (o dia vem primeiro) — sem isso, o DataTable
                    // comparava a string exibida e misturava meses/anos
                    // fora de ordem. "display" é só o que aparece na
                    // tela; "sort" é o valor real (timestamp) usado pra
                    // ordenar, nunca visto pelo usuário.
                    display: v => v ? new Date(v).toLocaleString('pt-BR') : '—',
                    sort: v => v ? new Date(v).getTime() : 0
                }
            },
            {
                data: null,
                orderable: false,
                render: (d, t, n) => {
                    const temPdf = n.status === STATUS_AUTORIZADA || n.status === STATUS_CANCELADA;
                    const botaoPdf = temPdf
                        ? `<a class="btn btn-sm btn-outline-secondary" href="/api/nfse/${n.id}/danfse-pdf" title="Baixar PDF (DANFSe)"><i class="bi bi-file-earmark-pdf"></i></a>`
                        + ` <a class="btn btn-sm btn-outline-secondary" href="/api/nfse/${n.id}/xml" download title="Baixar XML da nota"><i class="bi bi-filetype-xml"></i></a>`
                        : '';
                    // podeReenviar já vem resolvido pelo backend (rejeição
                    // segura de reenviar + ainda não reenviada) — a tela
                    // só soma a permissão de emitir.
                    const botaoReenviar = podeEmitirNfse && n.podeReenviar
                        ? `<button type="button" class="btn btn-sm btn-outline-warning btn-reenviar-nfse" data-id="${n.id}" title="Reenviar com os dados atuais do cadastro"><i class="bi bi-arrow-repeat"></i> Reenviar</button>`
                        : '';
                    // Manda (de novo) o PDF e o XML pro e-mail do tomador.
                    const botaoEmail = podeEmitirNfse && n.status === STATUS_AUTORIZADA
                        ? `<button type="button" class="btn btn-sm btn-outline-secondary btn-enviar-email-nfse" data-id="${n.id}" title="Enviar PDF e XML por e-mail ao cliente"><i class="bi bi-envelope"></i></button>`
                        : '';
                    // C7: substituir = emitir a nota certa e a SEFIN cancela esta (permissão de cancelar).
                    const botaoSubstituir = podeCancelarNfse && n.podeSubstituir
                        ? `<button type="button" class="btn btn-sm btn-outline-primary btn-substituir-nfse" data-id="${n.id}" title="Emitir a nota corrigida; a SEFIN cancela esta automaticamente"><i class="bi bi-arrow-left-right"></i></button>`
                        : '';
                    return `<button type="button" class="btn btn-sm btn-outline-secondary btn-ver-detalhe" data-id="${n.id}"><i class="bi bi-eye"></i> Detalhes</button> ${botaoPdf} ${botaoEmail} ${botaoSubstituir} ${botaoReenviar}`;
                }
            }
        ],
        // Sem isso, o DataTables cai no default (1ª coluna, checkbox,
        // não ordenável) — a API já manda mais recente primeiro, mas a
        // tabela reordenava na tela por cima disso. Índice 6 = coluna
        // "Emissão" (0-based: checkbox, número, cliente, valor,
        // valorLíquido, status, emissão, ações).
        order: [[6, 'desc']],
    });

    configurarExportacao(tabelaNfse, 'NotasFiscais');

    document.getElementById('tabela-nfse').addEventListener('click', async function (e) {
        const botao = e.target.closest('.btn-ver-detalhe');
        if (botao) await executarComBotaoDesabilitado(botao, () => abrirDetalheNfse(botao.dataset.id));

        const botaoReenviar = e.target.closest('.btn-reenviar-nfse');
        if (botaoReenviar) await executarComBotaoDesabilitado(botaoReenviar, () => abrirModalReenvioNfse(botaoReenviar.dataset.id));

        const botaoSubstituir = e.target.closest('.btn-substituir-nfse');
        if (botaoSubstituir) await executarComBotaoDesabilitado(botaoSubstituir, () => abrirModalSubstituicaoNfse(botaoSubstituir.dataset.id));

        const botaoEmail = e.target.closest('.btn-enviar-email-nfse');
        if (botaoEmail) await executarComBotaoDesabilitado(botaoEmail, () => enviarNfsePorEmail(botaoEmail.dataset.id));
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
    document.getElementById('btn-xml-mes').addEventListener('click', abrirXmlMes);
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
        });

        configurarSelectMunicipio({ select: 'emitir-localPrestacao', modal: 'modal-emitir-nfse' });

        document.getElementById('emitir-issRetido').addEventListener('change', function () {
            definirIssRetido(this.checked);
        });

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

    // Período padrão: últimos 30 dias, mesmo default do portal nacional
    // (tela "Notas recebidas") — familiar pra quem já usa os dois.
    const hoje = new Date();
    const trintaDiasAtras = new Date(hoje);
    trintaDiasAtras.setDate(trintaDiasAtras.getDate() - 30);
    document.getElementById('filtro-data-fim').value = hoje.toISOString().slice(0, 10);
    document.getElementById('filtro-data-inicio').value = trintaDiasAtras.toISOString().slice(0, 10);

    ['filtro-data-inicio', 'filtro-data-fim', 'filtro-status', 'filtro-cliente'].forEach(id =>
        document.getElementById(id).addEventListener('change', carregarNfse));

    document.getElementById('btn-limpar-filtros-nfse').addEventListener('click', function () {
        document.getElementById('filtro-data-inicio').value = trintaDiasAtras.toISOString().slice(0, 10);
        document.getElementById('filtro-data-fim').value = hoje.toISOString().slice(0, 10);
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
        selectFiltroCliente.innerHTML = '<option value="">Todos</option>' +
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

async function carregarNfse() {
    if (!empresaAtualIdNfse) return;
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
function mostrarResumoEmissao() {
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
            ${linha('Local da prestação', escaparHtml(textoMunicipioSelecionado('emitir-localPrestacao', 'Município da empresa')))}
            ${nfseSubstituicaoAtual ? linha('Substitui', escaparHtml(`DPS nº ${nfseSubstituicaoAtual.numeroDps} — ${textoSelecionado('emitir-motivoSubstituicao')}`)) : ''}
        </dl>
        <div class="mt-2">
            <div class="form-label mb-1">Descrição que vai na nota</div>
            <div class="descricao-nota">${escaparHtml(document.getElementById('emitir-descricaoServico').value)}</div>
        </div>`;

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
        mostrarResumoEmissao();
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
        codigoMunicipioPrestacao: $('#emitir-localPrestacao').val() || ''
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

        const totalFalhas = [...porContrato.values()].filter(rs => rs.some(r => !r.sucesso)).length;
        if (totalFalhas === 0) mostrarToast('Lote emitido com sucesso.');
        else mostrarToast(`Lote processado: ${totalFalhas} contrato(s) com falha — revise e emita de novo.`);
    } catch (err) {
        mostrarErroFormulario('erro-nota-mensal', err.message);
    } finally {
        atualizarBotaoEmitirNotaMensal();
    }
}

function atualizarBotaoBaixarSelecionadas() {
    document.getElementById('btn-baixar-selecionadas').disabled = nfseSelecionadas.size === 0;
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

/**
 * Botão "envelope" da grid: entra na fila de e-mails (PDF e XML anexos,
 * gerados na hora do envio). O resultado final aparece na tela de E-mails.
 */
async function enviarNfsePorEmail(id) {
    const confirmado = await confirmarAcao('Enviar o PDF e o XML desta nota para o e-mail do cliente?', { titulo: 'Enviar nota por e-mail', textoBotao: 'Enviar' });
    if (!confirmado) return;

    try {
        const resultado = await apiFetch(`/api/nfse/${id}/enviar-email`, { method: 'POST' });
        const copias = resultado.copias?.length ? ` (+${resultado.copias.length} em cópia)` : '';
        mostrarToast(`Nota enviada para ${resultado.destinatario}${copias}. Acompanhe a entrega na tela de E-mails.`);
    } catch (err) {
        mostrarErro(err.message);
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
