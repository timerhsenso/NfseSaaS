const podeEditarContrato = document.getElementById('btn-novo-contrato') !== null;

// SituacaoContrato (Domain.Enums) serializado como número pela API —
// mesmo padrão de ROTULOS_STATUS em nfse.js.
const ROTULOS_SITUACAO_CONTRATO = {
    0: { texto: 'Em dia', cor: 'success' },
    1: { texto: 'Vencendo em breve', cor: 'warning' },
    2: { texto: 'Vencido', cor: 'danger' }
};

let tabelaContratos;
let modalContrato;
let modalHistoricoReajuste;
let empresaAtualIdContratos;
let contratoAtualParaHistorico;

document.addEventListener('DOMContentLoaded', function () {
    const colunas = [
        { data: 'clienteNome' },
        { data: 'descricao' },
        { data: 'servicoDescricao' },
        { data: 'valorAtual', render: v => Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
        { data: 'dataProximoReajuste', render: v => v ? new Date(v + 'T00:00:00').toLocaleDateString('pt-BR') : '—' },
        {
            data: 'situacao',
            render: v => {
                const r = ROTULOS_SITUACAO_CONTRATO[v] ?? { texto: 'Desconhecida', cor: 'secondary' };
                return `<span class="badge text-bg-${r.cor}">${r.texto}</span>`;
            }
        },
        {
            data: 'ativo',
            render: v => v
                ? '<span class="badge text-bg-success">Ativo</span>'
                : '<span class="badge text-bg-secondary">Inativo</span>'
        }
    ];

    if (podeEditarContrato) {
        colunas.push({
            data: null,
            orderable: false,
            render: (data, type, contrato) => `
                <button type="button" class="btn btn-sm btn-outline-secondary btn-historico" data-id="${contrato.id}" data-descricao="${contrato.descricao}" title="Histórico de reajustes">
                    <i class="bi bi-clock-history"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-primary btn-editar" data-id="${contrato.id}">
                    <i class="bi bi-pencil"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-warning btn-alternar-ativo" data-id="${contrato.id}" data-ativo="${contrato.ativo}">
                    <i class="bi ${contrato.ativo ? 'bi-toggle-on' : 'bi-toggle-off'}"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-danger btn-excluir" data-id="${contrato.id}">
                    <i class="bi bi-trash"></i>
                </button>
            `
        });
    }

    tabelaContratos = new DataTable('#tabela-contratos', {
        columns: colunas,
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    configurarExportacao(tabelaContratos, 'Contratos');

    if (podeEditarContrato) {
        modalContrato = new bootstrap.Modal(document.getElementById('modal-contrato'));
        modalHistoricoReajuste = new bootstrap.Modal(document.getElementById('modal-historico-reajuste'));
        document.getElementById('btn-novo-contrato').addEventListener('click', abrirModalNovoContrato);
        document.getElementById('form-contrato').addEventListener('submit', salvarContrato);
        document.getElementById('form-reajuste').addEventListener('submit', registrarReajuste);

        document.getElementById('tabela-contratos').addEventListener('click', async function (e) {
            const botao = e.target.closest('button');
            if (!botao) return;
            const id = botao.dataset.id;

            if (botao.classList.contains('btn-editar')) await abrirModalEditarContrato(id);
            else if (botao.classList.contains('btn-alternar-ativo')) await alternarAtivoContrato(id, botao.dataset.ativo === 'true');
            else if (botao.classList.contains('btn-excluir')) await excluirContrato(id);
            else if (botao.classList.contains('btn-historico')) await abrirModalHistorico(id, botao.dataset.descricao);
        });
    }

    empresaAtualIdContratos = obterEmpresaAtualId();
    carregarContratos();
});

async function carregarContratos() {
    if (!empresaAtualIdContratos) return;

    try {
        const resultado = await apiFetch(`/api/contratos?empresaId=${empresaAtualIdContratos}&pageSize=200&incluirInativos=true`);
        tabelaContratos.clear();
        tabelaContratos.rows.add(resultado.items);
        tabelaContratos.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function limparFormularioContrato() {
    document.getElementById('form-contrato').reset();
    document.getElementById('contratoId').value = '';
    document.getElementById('periodicidadeReajusteMeses').value = 12;
    ocultarErroFormulario('erro-contrato');

    // Cliente e Valor só são editáveis no cadastro — depois de criado, o
    // valor muda exclusivamente pelo fluxo de Registrar reajuste (com
    // histórico), então nem faz sentido editar aqui.
    document.getElementById('clienteId').disabled = false;
    document.getElementById('valorAtual').disabled = false;
    document.getElementById('nota-valor-nao-editavel').textContent = '';
}

async function carregarOpcoesClienteEServico(clienteSelecionadoId) {
    const [clientes, servicos] = await Promise.all([
        apiFetch(`/api/clientes?empresaId=${empresaAtualIdContratos}&pageSize=200`),
        apiFetch(`/api/servicos?empresaId=${empresaAtualIdContratos}&pageSize=200`)
    ]);

    const selectCliente = document.getElementById('clienteId');
    selectCliente.innerHTML = clientes.items.map(c => `<option value="${c.id}">${c.nome} (${c.cpfCnpj})</option>`).join('');
    if (clienteSelecionadoId) selectCliente.value = clienteSelecionadoId;

    const selectServico = document.getElementById('servicoId');
    selectServico.innerHTML = servicos.items.map(s => `<option value="${s.id}" data-valor="${s.valorPadrao}">${s.descricao}</option>`).join('');
}

async function abrirModalNovoContrato() {
    limparFormularioContrato();
    document.getElementById('titulo-modal-contrato').textContent = 'Novo contrato';
    document.getElementById('dataInicioContrato').value = dataLocalIso();

    try {
        await carregarOpcoesClienteEServico();

        // Sugestão de ponto de partida a partir do valor padrão do
        // Serviço escolhido — o usuário ainda ajusta pro valor real
        // negociado com o cliente antes de salvar.
        const selectServico = document.getElementById('servicoId');
        selectServico.onchange = function () {
            const opcao = selectServico.selectedOptions[0];
            if (opcao?.dataset.valor) document.getElementById('valorAtual').value = opcao.dataset.valor;
        };
        selectServico.onchange();

        modalContrato.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function abrirModalEditarContrato(id) {
    limparFormularioContrato();
    document.getElementById('titulo-modal-contrato').textContent = 'Editar contrato';

    try {
        const contrato = await apiFetch(`/api/contratos/${id}`);
        await carregarOpcoesClienteEServico(contrato.clienteId);

        document.getElementById('contratoId').value = contrato.id;
        document.getElementById('servicoId').value = contrato.servicoId;
        document.getElementById('descricao').value = contrato.descricao;
        document.getElementById('valorAtual').value = contrato.valorAtual;
        document.getElementById('dataInicioContrato').value = contrato.dataInicioContrato;
        document.getElementById('periodicidadeReajusteMeses').value = contrato.periodicidadeReajusteMeses;
        document.getElementById('indiceReajuste').value = contrato.indiceReajuste ?? '';
        document.getElementById('diasAlertaOverride').value = contrato.diasAlertaOverride ?? '';

        document.getElementById('clienteId').disabled = true;
        document.getElementById('valorAtual').disabled = true;
        document.getElementById('nota-valor-nao-editavel').textContent =
            'Cliente e valor não são editáveis aqui — o valor muda pelo fluxo de reajuste (mantém histórico).';

        modalContrato.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function salvarContrato(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-contrato');

    const id = document.getElementById('contratoId').value;
    const indiceReajuste = document.getElementById('indiceReajuste').value.trim();
    const diasAlertaOverride = document.getElementById('diasAlertaOverride').value;

    try {
        if (id) {
            const payload = {
                servicoId: document.getElementById('servicoId').value,
                descricao: document.getElementById('descricao').value,
                dataInicioContrato: document.getElementById('dataInicioContrato').value,
                periodicidadeReajusteMeses: parseInt(document.getElementById('periodicidadeReajusteMeses').value, 10),
                indiceReajuste: indiceReajuste || null,
                diasAlertaOverride: diasAlertaOverride ? parseInt(diasAlertaOverride, 10) : null
            };
            await apiFetch(`/api/contratos/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            const payload = {
                empresaId: empresaAtualIdContratos,
                clienteId: document.getElementById('clienteId').value,
                servicoId: document.getElementById('servicoId').value,
                descricao: document.getElementById('descricao').value,
                valorAtual: parseFloat(document.getElementById('valorAtual').value),
                dataInicioContrato: document.getElementById('dataInicioContrato').value,
                periodicidadeReajusteMeses: parseInt(document.getElementById('periodicidadeReajusteMeses').value, 10),
                indiceReajuste: indiceReajuste || null,
                diasAlertaOverride: diasAlertaOverride ? parseInt(diasAlertaOverride, 10) : null
            };
            await apiFetch('/api/contratos', { method: 'POST', body: JSON.stringify(payload) });
        }

        modalContrato.hide();
        await carregarContratos();
        mostrarToast('Contrato salvo com sucesso.');
    } catch (err) {
        mostrarErroFormulario('erro-contrato', err.message);
    }
}

async function alternarAtivoContrato(id, ativoAtualmente) {
    const acao = ativoAtualmente ? 'desativar' : 'reativar';
    try {
        await apiFetch(`/api/contratos/${id}/${acao}`, { method: 'POST' });
        await carregarContratos();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function excluirContrato(id) {
    const confirmado = await confirmarAcao('Excluir este contrato?', { titulo: 'Excluir contrato', textoBotao: 'Excluir', variante: 'perigo' });
    if (!confirmado) return;

    try {
        await apiFetch(`/api/contratos/${id}`, { method: 'DELETE' });
        await carregarContratos();
        mostrarToast('Contrato excluído.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function abrirModalHistorico(contratoId, descricao) {
    contratoAtualParaHistorico = null;
    ocultarErroFormulario('erro-reajuste');
    document.getElementById('titulo-modal-historico').textContent = `Histórico de reajustes — ${descricao}`;
    document.getElementById('form-reajuste').reset();
    document.getElementById('reajuste-contratoId').value = contratoId;
    document.getElementById('reajuste-dataReajuste').value = dataLocalIso();

    try {
        const contrato = await apiFetch(`/api/contratos/${contratoId}`);
        contratoAtualParaHistorico = contrato;

        // % e valor novo se calculam um a partir do outro — digitar
        // qualquer um dos dois já atualiza o outro, usando o valor atual
        // do contrato como base.
        const campoValorNovo = document.getElementById('reajuste-valorNovo');
        const campoPercentual = document.getElementById('reajuste-percentual');

        campoValorNovo.oninput = function () {
            if (!campoValorNovo.value) return;
            const percentual = (parseFloat(campoValorNovo.value) / contrato.valorAtual - 1) * 100;
            campoPercentual.value = percentual.toFixed(2);
        };
        campoPercentual.oninput = function () {
            if (!campoPercentual.value) return;
            const valorNovo = contrato.valorAtual * (1 + parseFloat(campoPercentual.value) / 100);
            campoValorNovo.value = valorNovo.toFixed(2);
        };

        document.getElementById('reajuste-indiceUsado').value = contrato.indiceReajuste ?? '';

        await carregarHistorico(contratoId);
        modalHistoricoReajuste.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function carregarHistorico(contratoId) {
    const corpo = document.querySelector('#tabela-historico-reajuste tbody');

    try {
        const reajustes = await apiFetch(`/api/contratos/${contratoId}/reajustes`);

        if (reajustes.length === 0) {
            corpo.innerHTML = '<tr><td colspan="6" class="text-center text-muted">Nenhum reajuste registrado ainda.</td></tr>';
            return;
        }

        corpo.innerHTML = reajustes.map(r => `
            <tr>
                <td>${new Date(r.dataReajuste + 'T00:00:00').toLocaleDateString('pt-BR')}</td>
                <td>${Number(r.valorAnterior).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</td>
                <td>${Number(r.valorNovo).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</td>
                <td>${r.percentualAplicado != null ? r.percentualAplicado.toFixed(2) + '%' : '—'}</td>
                <td>${r.indiceUsado ?? '—'}</td>
                <td>${r.observacao ?? '—'}</td>
            </tr>
        `).join('');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function registrarReajuste(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-reajuste');

    const contratoId = document.getElementById('reajuste-contratoId').value;
    const payload = {
        dataReajuste: document.getElementById('reajuste-dataReajuste').value,
        valorNovo: parseFloat(document.getElementById('reajuste-valorNovo').value),
        indiceUsado: document.getElementById('reajuste-indiceUsado').value.trim() || null,
        observacao: document.getElementById('reajuste-observacao').value.trim() || null
    };

    try {
        await apiFetch(`/api/contratos/${contratoId}/reajustes`, { method: 'POST', body: JSON.stringify(payload) });

        document.getElementById('form-reajuste').reset();
        document.getElementById('reajuste-contratoId').value = contratoId;
        document.getElementById('reajuste-dataReajuste').value = dataLocalIso();

        await carregarHistorico(contratoId);
        await carregarContratos(); // valorAtual/situação mudaram na tabela principal também
        mostrarToast('Reajuste registrado com sucesso.');
    } catch (err) {
        mostrarErroFormulario('erro-reajuste', err.message);
    }
}
