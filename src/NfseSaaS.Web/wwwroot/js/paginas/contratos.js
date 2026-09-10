const podeEditarContrato = document.getElementById('btn-novo-contrato') !== null;

// SituacaoContrato (Domain.Enums) serializado como número pela API —
// mesmo padrão de ROTULOS_STATUS em nfse.js.
const ROTULOS_SITUACAO_CONTRATO = {
    0: { texto: 'Em dia', cor: 'success' },
    1: { texto: 'Vencendo em breve', cor: 'warning' },
    2: { texto: 'Vencido', cor: 'danger' }
};

// StatusContrato e TipoCobrancaContrato (Domain.Enums) serializados como
// número pela API — Fase 6.
const ROTULOS_STATUS_CONTRATO = {
    0: { texto: 'Rascunho', cor: 'secondary' },
    1: { texto: 'Ativo', cor: 'success' },
    2: { texto: 'Suspenso', cor: 'warning' },
    3: { texto: 'Encerrado', cor: 'dark' },
    4: { texto: 'Cancelado', cor: 'danger' }
};
const ROTULOS_TIPO_COBRANCA = { 0: 'Avulso', 1: 'Mensal' };

let tabelaContratos;
let modalContrato;
let modalHistoricoReajuste;
let empresaAtualIdContratos;
let contratoAtualParaHistorico;
let catalogoServicosContratos = []; // cache do combo, reaproveitado nas linhas dinâmicas

document.addEventListener('DOMContentLoaded', function () {
    const colunas = [
        { data: 'clienteNome' },
        { data: 'descricao' },
        {
            data: 'servicos',
            render: servicos => (servicos ?? []).map(s => s.servicoDescricao).join(', ') || '—'
        },
        { data: 'valorAtual', render: v => Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
        { data: 'tipoCobranca', render: v => ROTULOS_TIPO_COBRANCA[v] ?? '—' },
        {
            data: 'status',
            render: v => {
                const r = ROTULOS_STATUS_CONTRATO[v] ?? { texto: 'Desconhecido', cor: 'secondary' };
                return `<span class="badge text-bg-${r.cor}">${r.texto}</span>`;
            }
        },
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
        document.getElementById('btn-add-linha-servico').addEventListener('click', () => adicionarLinhaServico());

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
    document.getElementById('linhas-servico-contrato').innerHTML = '';
    ocultarErroFormulario('erro-contrato');
    recalcularValorAtualCalculado();

    // Cliente e as linhas de Serviço só são editáveis no cadastro —
    // depois de criado, o valor muda exclusivamente pelo fluxo de
    // Registrar reajuste (com histórico) e a composição de serviços fica
    // pra um endpoint dedicado futuro (Fase 6, mesmo raciocínio já usado
    // pro ValorAtual antes de existir linhas).
    document.getElementById('clienteId').disabled = false;
    document.getElementById('bloco-servicos').classList.remove('d-none');
    document.getElementById('bloco-status').classList.add('d-none');
    document.getElementById('nota-servicos-nao-editaveis').textContent = '';
}

async function carregarOpcoesClienteEServico(clienteSelecionadoId) {
    const [clientes, servicos] = await Promise.all([
        apiFetch(`/api/clientes?empresaId=${empresaAtualIdContratos}&pageSize=200`),
        apiFetch(`/api/servicos?empresaId=${empresaAtualIdContratos}&pageSize=200`)
    ]);

    const selectCliente = document.getElementById('clienteId');
    selectCliente.innerHTML = clientes.items.map(c => `<option value="${c.id}">${c.nome} (${c.cpfCnpj})</option>`).join('');
    if (clienteSelecionadoId) selectCliente.value = clienteSelecionadoId;

    catalogoServicosContratos = servicos.items;
}

// Fase 6: uma linha = 1 serviço do contrato (ServicoId + Quantidade +
// ValorUnitario). Sugere o valorPadrao do Serviço escolhido como ponto
// de partida — o usuário ainda ajusta pro valor real negociado. Mesmo
// Serviço não pode aparecer em duas linhas (ver
// atualizarOpcoesServicosDisponiveis) — mesma regra validada no backend
// (CadastrarContratoRequestValidator), isso aqui é só UX.
function adicionarLinhaServico(linha) {
    const corpo = document.getElementById('linhas-servico-contrato');

    const tr = document.createElement('tr');
    tr.className = 'linha-servico-contrato';
    tr.innerHTML = `
        <td><select class="form-select form-select-sm campo-linha-servico"></select></td>
        <td><input type="number" step="0.01" min="0.01" class="form-control form-control-sm campo-linha-quantidade" value="${linha?.quantidade ?? 1}" /></td>
        <td><input type="number" step="0.01" min="0.01" class="form-control form-control-sm campo-linha-valor" value="${linha?.valorUnitario ?? ''}" /></td>
        <td class="text-end campo-linha-total">R$ 0,00</td>
        <td><button type="button" class="btn btn-sm btn-outline-danger btn-remover-linha-servico"><i class="bi bi-x-lg"></i></button></td>
    `;
    tr.dataset.servicoSelecionado = linha?.servicoId ?? '';
    corpo.appendChild(tr);

    const selectServico = tr.querySelector('.campo-linha-servico');
    const campoValor = tr.querySelector('.campo-linha-valor');

    selectServico.addEventListener('change', () => {
        tr.dataset.servicoSelecionado = selectServico.value;
        if (!linha) {
            const opcao = selectServico.selectedOptions[0];
            if (opcao?.dataset.valor) campoValor.value = opcao.dataset.valor;
        }
        atualizarOpcoesServicosDisponiveis();
        recalcularValorAtualCalculado();
    });

    tr.querySelector('.btn-remover-linha-servico').addEventListener('click', () => {
        tr.remove();
        atualizarOpcoesServicosDisponiveis();
        recalcularValorAtualCalculado();
    });
    tr.querySelector('.campo-linha-quantidade').addEventListener('input', recalcularValorAtualCalculado);
    campoValor.addEventListener('input', recalcularValorAtualCalculado);

    atualizarOpcoesServicosDisponiveis();
    recalcularValorAtualCalculado();
}

// Reconstrói as opções de cada <select> de linha: um Serviço já
// escolhido em OUTRA linha fica desabilitado (visível, pra não confundir
// sumindo do combo, mas não selecionável) — a própria linha continua
// podendo manter o Serviço que ela já tem selecionado.
function atualizarOpcoesServicosDisponiveis() {
    const linhas = Array.from(document.querySelectorAll('#linhas-servico-contrato .linha-servico-contrato'));
    const selecionadosPorLinha = linhas.map(tr => tr.dataset.servicoSelecionado || '');

    linhas.forEach((tr, i) => {
        const select = tr.querySelector('.campo-linha-servico');
        const atual = selecionadosPorLinha[i];
        const usadosEmOutrasLinhas = new Set(selecionadosPorLinha.filter((id, j) => id && j !== i));

        select.innerHTML = catalogoServicosContratos.map(s => {
            const desabilitado = usadosEmOutrasLinhas.has(s.id);
            const selecionado = s.id === atual;
            return `<option value="${s.id}" data-valor="${s.valorPadrao}" ${selecionado ? 'selected' : ''} ${desabilitado ? 'disabled' : ''}>${s.descricao}${desabilitado ? ' (já usado noutra linha)' : ''}</option>`;
        }).join('');

        if (!atual && select.options.length > 0) {
            // Linha nova sem seleção: escolhe automaticamente o primeiro
            // serviço ainda disponível, pra não deixar cair num que já
            // está desabilitado.
            const primeiraDisponivel = Array.from(select.options).find(o => !o.disabled);
            if (primeiraDisponivel) {
                select.value = primeiraDisponivel.value;
                tr.dataset.servicoSelecionado = primeiraDisponivel.value;

                // Atualiza o snapshot AGORA, dentro do próprio laço — senão
                // duas linhas novas adicionadas na mesma chamada (ex.:
                // "Adicionar serviço" clicado 2x seguidas) veem o mesmo
                // estado "antes de decidir" e acabam escolhendo o mesmo
                // serviço uma da outra (bug real, reportado pelo usuário).
                selecionadosPorLinha[i] = primeiraDisponivel.value;

                // Preenche o Valor unit. com o valorPadrao do Serviço
                // auto-selecionado — só quando o campo está vazio, pra não
                // sobrescrever um valor que o usuário já tinha digitado.
                const campoValor = tr.querySelector('.campo-linha-valor');
                if (campoValor && !campoValor.value) {
                    campoValor.value = primeiraDisponivel.dataset.valor ?? '';
                }
            }
        }
    });
}

function coletarLinhasServico() {
    return Array.from(document.querySelectorAll('#linhas-servico-contrato .linha-servico-contrato')).map(tr => ({
        servicoId: tr.querySelector('.campo-linha-servico').value,
        quantidade: parseFloat(tr.querySelector('.campo-linha-quantidade').value) || 0,
        valorUnitario: parseFloat(tr.querySelector('.campo-linha-valor').value) || 0
    }));
}

function recalcularValorAtualCalculado() {
    document.querySelectorAll('#linhas-servico-contrato .linha-servico-contrato').forEach(tr => {
        const quantidade = parseFloat(tr.querySelector('.campo-linha-quantidade').value) || 0;
        const valorUnitario = parseFloat(tr.querySelector('.campo-linha-valor').value) || 0;
        tr.querySelector('.campo-linha-total').textContent = (quantidade * valorUnitario).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    });

    const total = coletarLinhasServico().reduce((soma, l) => soma + l.quantidade * l.valorUnitario, 0);
    document.getElementById('valorAtualCalculado').textContent = total.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

async function abrirModalNovoContrato() {
    limparFormularioContrato();
    document.getElementById('titulo-modal-contrato').textContent = 'Novo contrato';
    document.getElementById('dataInicioContrato').value = dataLocalIso();

    try {
        await carregarOpcoesClienteEServico();
        adicionarLinhaServico();
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
        document.getElementById('descricao').value = contrato.descricao;
        document.getElementById('dataInicioContrato').value = contrato.dataInicioContrato;
        document.getElementById('dataFim').value = contrato.dataFim ?? '';
        document.getElementById('tipoCobranca').value = contrato.tipoCobranca;
        document.getElementById('status').value = contrato.status;
        document.getElementById('periodicidadeReajusteMeses').value = contrato.periodicidadeReajusteMeses;
        document.getElementById('indiceReajuste').value = contrato.indiceReajuste ?? '';
        document.getElementById('diasAlertaOverride').value = contrato.diasAlertaOverride ?? '';
        document.getElementById('permitirAlterarValorNaEmissao').checked = contrato.permitirAlterarValorNaEmissao;

        (contrato.servicos ?? []).forEach(s => adicionarLinhaServico(s));

        document.getElementById('clienteId').disabled = true;
        document.getElementById('bloco-servicos').classList.add('d-none');
        document.getElementById('bloco-status').classList.remove('d-none');
        document.getElementById('nota-servicos-nao-editaveis').textContent =
            'Cliente e serviços não são editáveis aqui — o valor muda pelo fluxo de reajuste (mantém histórico).';

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

    const linhasServico = coletarLinhasServico();
    if (!id) {
        const idsUnicos = new Set(linhasServico.map(l => l.servicoId));
        if (idsUnicos.size !== linhasServico.length) {
            mostrarErroFormulario('erro-contrato', 'O mesmo serviço não pode aparecer em mais de uma linha.');
            return;
        }
    }

    try {
        if (id) {
            const payload = {
                descricao: document.getElementById('descricao').value,
                dataInicioContrato: document.getElementById('dataInicioContrato').value,
                periodicidadeReajusteMeses: parseInt(document.getElementById('periodicidadeReajusteMeses').value, 10),
                indiceReajuste: indiceReajuste || null,
                diasAlertaOverride: diasAlertaOverride ? parseInt(diasAlertaOverride, 10) : null,
                status: parseInt(document.getElementById('status').value, 10),
                dataFim: document.getElementById('dataFim').value || null,
                tipoCobranca: parseInt(document.getElementById('tipoCobranca').value, 10),
                permitirAlterarValorNaEmissao: document.getElementById('permitirAlterarValorNaEmissao').checked
            };
            await apiFetch(`/api/contratos/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            const payload = {
                empresaId: empresaAtualIdContratos,
                clienteId: document.getElementById('clienteId').value,
                descricao: document.getElementById('descricao').value,
                servicos: linhasServico,
                dataInicioContrato: document.getElementById('dataInicioContrato').value,
                periodicidadeReajusteMeses: parseInt(document.getElementById('periodicidadeReajusteMeses').value, 10),
                indiceReajuste: indiceReajuste || null,
                diasAlertaOverride: diasAlertaOverride ? parseInt(diasAlertaOverride, 10) : null,
                dataFim: document.getElementById('dataFim').value || null,
                tipoCobranca: parseInt(document.getElementById('tipoCobranca').value, 10),
                permitirAlterarValorNaEmissao: document.getElementById('permitirAlterarValorNaEmissao').checked
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
