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

document.addEventListener('DOMContentLoaded', function () {
    tabelaNfse = new DataTable('#tabela-nfse', {
        columns: [
            { data: null, render: (d, t, n) => `${n.numeroDps}/${n.serieDps}` },
            { data: 'clienteId', render: id => clientesPorId[id] ?? id },
            { data: 'valorServico', render: v => Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
            { data: 'valorLiquido', render: v => v == null ? '—' : Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
            {
                data: 'status',
                render: v => {
                    const r = ROTULOS_STATUS[v] ?? { texto: 'Desconhecido', cor: 'secondary' };
                    return `<span class="badge text-bg-${r.cor}">${r.texto}</span>`;
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
                render: (d, t, n) => `<button type="button" class="btn btn-sm btn-outline-secondary btn-ver-detalhe" data-id="${n.id}"><i class="bi bi-eye"></i> Detalhes</button>`
            }
        ],
        // Sem isso, o DataTables cai no default (1ª coluna, Número/Série,
        // crescente) — a API já manda mais recente primeiro, mas a
        // tabela reordenava na tela por cima disso. Índice 5 = coluna
        // "Emissão" (0-based: número, cliente, valor, valorLíquido,
        // status, emissão, ações).
        order: [[5, 'desc']],
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    configurarExportacao(tabelaNfse, 'NotasFiscais');

    document.getElementById('tabela-nfse').addEventListener('click', async function (e) {
        const botao = e.target.closest('.btn-ver-detalhe');
        if (botao) await abrirDetalheNfse(botao.dataset.id);
    });

    modalDetalheNfse = new bootstrap.Modal(document.getElementById('modal-detalhe-nfse'));

    if (podeEmitirNfse) {
        modalEmitirNfse = new bootstrap.Modal(document.getElementById('modal-emitir-nfse'));
        document.getElementById('btn-emitir-nfse').addEventListener('click', abrirModalEmitirNfse);
        document.getElementById('form-emitir-nfse').addEventListener('submit', emitirNfse);
        document.getElementById('btn-sincronizar-sefin').addEventListener('click', sincronizarComSefin);
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
        for (const c of resultado.items) clientesPorId[c.id] = c.nome;
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function carregarNfse() {
    if (!empresaAtualIdNfse) return;
    try {
        const resultado = await apiFetch(`/api/nfse?empresaId=${empresaAtualIdNfse}&pageSize=200`);
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

let contratosDoClienteAtual = [];

async function abrirModalEmitirNfse() {
    document.getElementById('form-emitir-nfse').reset();
    document.getElementById('erro-emitir-nfse').classList.add('d-none');
    document.getElementById('emitir-dataCompetencia').value = dataLocalIso();

    try {
        const [clientes, servicos] = await Promise.all([
            apiFetch(`/api/clientes?empresaId=${empresaAtualIdNfse}&pageSize=200`),
            apiFetch(`/api/servicos?empresaId=${empresaAtualIdNfse}&pageSize=200`)
        ]);

        const selectCliente = document.getElementById('emitir-clienteId');
        selectCliente.innerHTML = clientes.items.map(c => `<option value="${c.id}">${c.nome} (${c.cpfCnpj})</option>`).join('');

        const selectServico = document.getElementById('emitir-servicoId');
        selectServico.innerHTML = servicos.items.map(s => `<option value="${s.id}" data-descricao="${s.descricao}" data-valor="${s.valorPadrao}">${s.descricao}</option>`).join('');

        // Ao trocar o serviço MANUALMENTE, pré-preenche descrição/valor
        // com o padrão do catálogo — só entra em jogo quando não há
        // Contrato selecionado (ver selectContrato.onchange abaixo, que
        // sobrescreve isso quando o usuário escolhe um Contrato).
        selectServico.onchange = function () {
            const opcao = selectServico.selectedOptions[0];
            document.getElementById('emitir-descricaoServico').value = opcao?.dataset.descricao ?? '';
            document.getElementById('emitir-valorServico').value = opcao?.dataset.valor ?? '';
        };

        // Trocar de Cliente busca os Contratos ativos dele — o combo de
        // Contrato é opcional e some quando o Cliente não tem nenhum
        // (nada muda pra quem nunca usa Contrato).
        selectCliente.onchange = () => carregarContratosDoCliente(selectCliente.value);
        await selectCliente.onchange();

        modalEmitirNfse.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function carregarContratosDoCliente(clienteId) {
    const campoContrato = document.getElementById('campo-emitir-contrato');
    const selectContrato = document.getElementById('emitir-contratoId');
    const selectServico = document.getElementById('emitir-servicoId');

    try {
        const resultado = await apiFetch(`/api/contratos?empresaId=${empresaAtualIdNfse}&clienteId=${clienteId}&pageSize=50`);
        contratosDoClienteAtual = resultado.items;

        if (contratosDoClienteAtual.length === 0) {
            // Sem Contrato nenhum pra este Cliente — nem mostra a opção,
            // fluxo continua 100% manual (Serviço + valor), como sempre foi.
            campoContrato.classList.add('d-none');
            selectContrato.innerHTML = '';
            document.getElementById('aviso-reajuste-contrato').classList.add('d-none');
            selectServico.onchange();
            return;
        }

        campoContrato.classList.remove('d-none');
        selectContrato.innerHTML =
            '<option value="">Nenhum (nota avulsa)</option>' +
            contratosDoClienteAtual.map(c => `<option value="${c.id}">${c.descricao} — ${Number(c.valorAtual).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</option>`).join('');

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

function aplicarContratoSelecionado() {
    const selectContrato = document.getElementById('emitir-contratoId');
    const avisoDiv = document.getElementById('aviso-reajuste-contrato');
    const contratoId = selectContrato.value;

    if (!contratoId) {
        // "Nenhum (nota avulsa)" — volta pro fluxo manual de Serviço.
        avisoDiv.classList.add('d-none');
        document.getElementById('emitir-servicoId').onchange();
        return;
    }

    const contrato = contratosDoClienteAtual.find(c => c.id === contratoId);
    if (!contrato) return;

    document.getElementById('emitir-servicoId').value = contrato.servicoId;
    document.getElementById('emitir-descricaoServico').value = contrato.descricao;
    document.getElementById('emitir-valorServico').value = contrato.valorAtual;

    if (contrato.situacao === 0) {
        avisoDiv.classList.add('d-none');
    } else {
        const rotulo = ROTULOS_SITUACAO_CONTRATO[contrato.situacao] ?? { texto: 'com situação desconhecida', classe: 'alert-warning' };
        const dataFormatada = new Date(contrato.dataProximoReajuste + 'T00:00:00').toLocaleDateString('pt-BR');
        avisoDiv.className = `alert ${rotulo.classe}`;
        avisoDiv.innerHTML =
            `<i class="bi bi-exclamation-triangle"></i> Este contrato está <strong>${rotulo.texto}</strong> ` +
            `pro reajuste (previsto para ${dataFormatada}${contrato.indiceReajuste ? ', índice ' + contrato.indiceReajuste : ''}). ` +
            `Confirme se o valor abaixo já está atualizado.`;
        avisoDiv.classList.remove('d-none');
    }
}

async function emitirNfse(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-emitir-nfse');

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
        // Chave de idempotência própria desta tentativa de clique — se o
        // usuário reenviar (ex.: timeout + segundo clique acidental), a
        // API reconhece a mesma tentativa e não duplica a nota.
        idempotencyKey: crypto.randomUUID()
    };

    try {
        const resultado = await apiFetch('/api/nfse/emitir', { method: 'POST', body: JSON.stringify(payload) });

        modalEmitirNfse.hide();
        await carregarNfse();

        if (resultado.sucesso) {
            mostrarToast('Nota fiscal emitida com sucesso.');
        } else {
            mostrarErro(`Nota rejeitada pela SEFIN: ${resultado.mensagemErro ?? resultado.codigoErro ?? 'motivo não informado'}.`);
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

        const rotulo = ROTULOS_STATUS[nfse.status] ?? { texto: 'Desconhecido', cor: 'secondary' };

        document.getElementById('detalhe-nfse-dados').innerHTML = `
            <dt class="col-sm-4">Número/Série</dt><dd class="col-sm-8">${nfse.numeroDps}/${nfse.serieDps}</dd>
            <dt class="col-sm-4">Status</dt><dd class="col-sm-8"><span class="badge text-bg-${rotulo.cor}">${rotulo.texto}</span></dd>
            <dt class="col-sm-4">Chave de acesso</dt><dd class="col-sm-8">${nfse.chaveAcesso ?? '—'}</dd>
            <dt class="col-sm-4">Valor / Líquido</dt><dd class="col-sm-8">
                ${Number(nfse.valorServico).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })} /
                ${nfse.valorLiquido == null ? '—' : Number(nfse.valorLiquido).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}
            </dd>
            <dt class="col-sm-4">Erro</dt><dd class="col-sm-8">${nfse.mensagemErro ? `${nfse.codigoErro ?? ''} — ${nfse.mensagemErro}` : '—'}</dd>
        `;

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

        const btnCancelar = document.getElementById('btn-cancelar-nfse');
        if (btnCancelar) btnCancelar.classList.toggle('d-none', nfse.status !== STATUS_AUTORIZADA);

        // PDF só existe pra nota Autorizada ou Cancelada (mesma regra do
        // backend) — Rascunho/Processando/Rejeitada não têm ChaveAcesso.
        const linkPdf = document.getElementById('link-baixar-danfse');
        const temPdfDisponivel = nfse.status === STATUS_AUTORIZADA || nfse.status === STATUS_CANCELADA;
        linkPdf.href = temPdfDisponivel ? `/api/nfse/${id}/danfse-pdf` : '#';
        linkPdf.classList.toggle('disabled', !temPdfDisponivel);
        linkPdf.setAttribute('aria-disabled', String(!temPdfDisponivel));

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
            mostrarErro(`Cancelamento rejeitado pela SEFIN: ${resultado.mensagemErro ?? resultado.codigoErro ?? 'motivo não informado'}.`);
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
