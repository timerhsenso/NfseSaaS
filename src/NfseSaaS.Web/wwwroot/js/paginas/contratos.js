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

// IndiceReajusteContrato (Domain.Enums) serializado como número — Fase 6
// parte 2 (virou enum a pedido do usuário, era texto livre).
const ROTULOS_INDICE_REAJUSTE = { 0: 'IPCA', 1: 'IGPM', 2: 'INCC', 3: 'INPC', 4: 'SELIC', 5: 'CDI', 99: 'Outro' };

let tabelaContratos;
let modalContrato;
let modalHistoricoReajuste;

// Linhas do contrato como vieram do servidor ao abrir a edição — pra saber,
// ao salvar, se a composição mudou (aí a mudança precisa de tipo,
// vigência e motivo, e vira um evento do Histórico de valores).
let linhasOriginaisContrato = null;

function normalizarLinhas(linhas) {
    return JSON.stringify((linhas ?? [])
        .map(l => ({ servicoId: l.servicoId, quantidade: Number(l.quantidade), valorUnitario: Number(l.valorUnitario) }))
        .sort((a, b) => a.servicoId.localeCompare(b.servicoId)));
}
let empresaAtualIdContratos;
let contratoAtualParaHistorico;
// Evento "aguardando aprovação" do contrato aberto no Histórico de valores.
let reajustePendenteAtual = null;
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
            render: (v, type, contrato) => {
                const r = ROTULOS_SITUACAO_CONTRATO[v] ?? { texto: 'Desconhecida', cor: 'secondary' };
                const pendente = contrato.reajustePendenteId
                    ? ` <span class="badge text-bg-warning" title="Abra o Histórico de valores para aplicar, negociar ou recusar">Reajuste a aprovar</span>`
                    : '';
                return `<span class="badge text-bg-${r.cor}">${r.texto}</span>${pendente}`;
            }
        }
    ];

    if (podeEditarContrato) {
        colunas.push({
            data: null,
            orderable: false,
            render: (data, type, contrato) => `
                <button type="button" class="btn btn-sm btn-outline-secondary btn-historico" data-id="${contrato.id}" data-descricao="${contrato.descricao}" title="Histórico de valores e reajustes">
                    <i class="bi bi-clock-history"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-primary btn-editar" data-id="${contrato.id}">
                    <i class="bi bi-pencil"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-danger btn-excluir" data-id="${contrato.id}">
                    <i class="bi bi-trash"></i>
                </button>
            `
        });
    }

    tabelaContratos = new DataTable('#tabela-contratos', {
        columns: colunas,
    });

    configurarExportacao(tabelaContratos, 'Contratos');

    if (podeEditarContrato) {
        modalContrato = new bootstrap.Modal(document.getElementById('modal-contrato'));
        modalHistoricoReajuste = new bootstrap.Modal(document.getElementById('modal-historico-reajuste'));
        document.getElementById('btn-novo-contrato').addEventListener('click', abrirModalNovoContrato);
        document.getElementById('form-contrato').addEventListener('submit', salvarContrato);
        document.getElementById('form-reajuste').addEventListener('submit', registrarReajuste);
        document.getElementById('btn-verificar-reajustes').addEventListener('click', verificarReajustes);
        configurarPainelReajustePendente();
        document.getElementById('btn-add-linha-servico').addEventListener('click', () => adicionarLinhaServico());
        document.getElementById('btn-upload-documento').addEventListener('click', enviarDocumentoContrato);

        document.getElementById('tabs-contrato').addEventListener('click', function (e) {
            const botao = e.target.closest('button[data-tab-alvo]');
            if (botao) trocarAbaContrato(botao.dataset.tabAlvo);
        });

        document.getElementById('tipoCobranca').addEventListener('change', aplicarRegraTipoCobranca);

        // dropdownParent: mesmo motivo do select2 em servicos.js — sem
        // isso o dropdown nasce fora do modal do Bootstrap. minimumInputLength
        // evita carregar a lista de clientes inteira só de abrir o campo
        // (empresaId sempre filtra pra Empresa atual, igual o resto da tela).
        configurarSelectMunicipio({ select: 'localPrestacaoContrato', modal: 'modal-contrato' });

        $('#clienteId').select2({
            dropdownParent: $('#modal-contrato'),
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
                data: params => ({ empresaId: empresaAtualIdContratos, busca: params.term, pageSize: 20 }),
                processResults: data => ({
                    results: data.items.map(c => ({ id: c.id, text: `${c.apelido} (${c.cpfCnpj})` }))
                })
            }
        });

        document.getElementById('lista-documentos-contrato').addEventListener('click', async function (e) {
            const botao = e.target.closest('button.btn-excluir-documento');
            if (botao) await executarComBotaoDesabilitado(botao, () => excluirDocumentoContrato(botao.dataset.id));
        });

        document.getElementById('tabela-contratos').addEventListener('click', async function (e) {
            const botao = e.target.closest('button');
            if (!botao) return;
            const id = botao.dataset.id;

            await executarComBotaoDesabilitado(botao, async () => {
                if (botao.classList.contains('btn-editar')) await abrirModalEditarContrato(id);
                else if (botao.classList.contains('btn-excluir')) await excluirContrato(id);
                else if (botao.classList.contains('btn-historico')) await abrirModalHistorico(id, botao.dataset.descricao);
            });
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

function trocarAbaContrato(alvo) {
    document.querySelectorAll('#tabs-contrato .nav-link').forEach(b => b.classList.toggle('active', b.dataset.tabAlvo === alvo));
    document.querySelectorAll('.tab-conteudo-contrato').forEach(p => p.classList.toggle('d-none', p.dataset.tab !== alvo));
}

// Reajuste (periodicidade/índice/alerta) só faz sentido pra cobrança
// Mensal — Avulso é emitido uma vez, não tem recorrência pra reajustar.
// "Permitir alterar valor na emissão em lote" só importa pra Mensal
// também (Avulso nunca entra na emissão em lote, ver TipoCobrancaContrato)
// — por isso fica travado marcado, não é uma escolha real nesse caso.
// Chamado ao trocar o <select>, e também ao abrir/editar o modal (ver
// limparFormularioContrato e abrirModalEditarContrato) pra já nascer
// no estado certo.
function aplicarRegraTipoCobranca() {
    const avulso = document.getElementById('tipoCobranca').value === '0';

    const periodicidade = document.getElementById('periodicidadeReajusteMeses');
    const indice = document.getElementById('indiceReajuste');
    const diasAlerta = document.getElementById('diasAlertaOverride');
    const permitirAlterar = document.getElementById('permitirAlterarValorNaEmissao');
    const reajusteAutomatico = document.getElementById('reajusteAutomatico');
    const aplicarIndiceNegativo = document.getElementById('aplicarIndiceNegativo');

    reajusteAutomatico.disabled = avulso;
    aplicarIndiceNegativo.disabled = avulso;

    periodicidade.disabled = avulso;
    periodicidade.required = !avulso;
    indice.disabled = avulso;
    indice.required = !avulso;
    diasAlerta.disabled = avulso;
    permitirAlterar.disabled = avulso;

    if (avulso) {
        periodicidade.value = 0;
        indice.value = '';
        diasAlerta.value = '';
        permitirAlterar.checked = true;
        reajusteAutomatico.value = '';
        aplicarIndiceNegativo.checked = false;
    }
}

function limparFormularioContrato() {
    document.getElementById('form-contrato').reset();
    document.getElementById('contratoId').value = '';
    document.getElementById('periodicidadeReajusteMeses').value = 12;
    document.getElementById('linhas-servico-contrato').innerHTML = '';
    document.getElementById('observacao').value = '';
    ocultarErroFormulario('erro-contrato');
    recalcularValorAtualCalculado();
    trocarAbaContrato('tab-dados');
    aplicarRegraTipoCobranca();

    document.getElementById('clienteId').innerHTML = '';
    $('#clienteId').val(null).trigger('change');

    // Documentos e Histórico só existem depois que o Contrato tem Id.
    document.getElementById('documentos-indisponivel').classList.remove('d-none');
    document.getElementById('documentos-disponivel').classList.add('d-none');
    document.getElementById('historico-indisponivel').classList.remove('d-none');
    document.getElementById('tabela-historico-contrato').classList.add('d-none');

    // Cliente só é editável no cadastro. As linhas de Serviço são
    // editáveis no cadastro E na edição (ver abrirModalEditarContrato).
    $('#clienteId').prop('disabled', false);
    document.getElementById('bloco-servicos').classList.remove('d-none');
    document.getElementById('nota-servicos-nao-editaveis').textContent = '';
    document.getElementById('bloco-mudanca-servicos').classList.add('d-none');
    linhasOriginaisContrato = null;

    // Status agora é sempre visível (criação e edição) — antes só
    // aparecia editando, e criar sem escolher status nenhum criava
    // sempre "Ativo" nos bastidores sem o usuário nem saber que existia
    // essa opção.
    document.getElementById('status').value = '1';
}

// Cliente não entra mais aqui — select2 (ver inicialização acima) busca
// direto na API conforme o usuário digita. Só o catálogo de Serviços da
// Empresa atual continua pré-carregado (usado pelas linhas de serviço
// do contrato, catalogoServicosContratos).
async function carregarServicosContrato() {
    const servicos = await apiFetch(`/api/servicos?empresaId=${empresaAtualIdContratos}&pageSize=200`);
    catalogoServicosContratos = servicos.items;
}

// select2 com fonte remota não sabe o texto de um cliente já
// selecionado sem perguntar pra API — mas ContratoResponse já traz
// ClienteNome embutido (join feito no backend), então não precisa de
// uma chamada extra só pra isso, diferente do padrão usado pros
// catálogos de cTribNac/NBS em servicos.js.
function preencherClienteAtual(clienteId, clienteNome) {
    const select = document.getElementById('clienteId');
    select.innerHTML = '';
    select.appendChild(new Option(clienteNome, clienteId, true, true));
    $(select).trigger('change');
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
        <td class="text-center"><input type="checkbox" class="form-check-input campo-linha-variavel" ${linha?.valorVariavel ? 'checked' : ''}
            title="Valor muda a cada nota: na Nota Mensal vem o último valor emitido e a automação não emite sozinha" /></td>
        <td><button type="button" class="btn btn-sm btn-outline-danger btn-remover-linha-servico"><i class="bi bi-x-lg"></i></button></td>
    `;
    tr.dataset.servicoSelecionado = linha?.servicoId ?? '';
    // Guardado pra linha cujo Serviço foi DESATIVADO depois: ele não vem
    // no catálogo (só ativos) e precisa continuar aparecendo pelo nome.
    tr.dataset.servicoDescricao = linha?.servicoDescricao ?? '';
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

        // Linha existente com Serviço desativado: mantém a opção (pelo
        // nome gravado na linha) em vez de a linha aparecer em branco e
        // travar o salvamento. Montada com DOM, não innerHTML — o nome
        // vem do cadastro.
        if (atual && !catalogoServicosContratos.some(s => s.id === atual)) {
            const opcaoInativa = new Option(`${tr.dataset.servicoDescricao || 'Serviço'} (inativo)`, atual, true, true);
            select.prepend(opcaoInativa);
        }

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

    // Não deixa clicar em "Adicionar serviço" se todos os serviços ativos
    // do catálogo já estão em uso em alguma linha — não haveria opção
    // disponível pra escolher na linha nova.
    const btnAdd = document.getElementById('btn-add-linha-servico');
    btnAdd.disabled = catalogoServicosContratos.length > 0 && linhas.length >= catalogoServicosContratos.length;
}

function coletarLinhasServico() {
    return Array.from(document.querySelectorAll('#linhas-servico-contrato .linha-servico-contrato')).map(tr => ({
        servicoId: tr.querySelector('.campo-linha-servico').value,
        quantidade: parseFloat(tr.querySelector('.campo-linha-quantidade').value) || 0,
        valorUnitario: parseFloat(tr.querySelector('.campo-linha-valor').value) || 0,
        // Não entra em normalizarLinhas: mudar só isto não vira evento do Histórico de valores.
        valorVariavel: tr.querySelector('.campo-linha-variavel').checked
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

// Resumo fixo à direita do modal (só na edição): valor, situação, próximo
// reajuste e o reajuste pendente, se houver — o contexto do contrato fica
// à vista em qualquer aba.
function renderizarResumoContrato(contrato) {
    const resumo = document.getElementById('resumo-contrato');
    if (!contrato) {
        resumo.classList.add('d-none');
        return;
    }

    const situacao = ROTULOS_SITUACAO_CONTRATO[contrato.situacao] ?? { texto: '—', cor: 'secondary' };
    const status = ROTULOS_STATUS_CONTRATO[contrato.status] ?? { texto: '—', cor: 'secondary' };
    const qtdServicos = (contrato.servicos ?? []).length;
    const aprovacao = contrato.reajusteAutomatico == null
        ? 'Padrão da empresa'
        : contrato.reajusteAutomatico ? 'Aplica automaticamente' : 'Aguarda aprovação';

    document.getElementById('resumo-valor-contrato').textContent = formatarReais(contrato.valorAtual);
    document.getElementById('resumo-detalhe-contrato').textContent =
        `${qtdServicos} serviço(s) · ${ROTULOS_TIPO_COBRANCA[contrato.tipoCobranca] ?? ''} · desde ${new Date(contrato.dataInicioContrato + 'T00:00:00').toLocaleDateString('pt-BR')}`;
    document.getElementById('resumo-situacao-contrato').innerHTML =
        `<span class="badge text-bg-${status.cor}">${status.texto}</span>`;
    const avulso = contrato.tipoCobranca === 0;
    document.getElementById('resumo-proximo-contrato').innerHTML = avulso
        ? '—'
        : `${new Date(contrato.dataProximoReajuste + 'T00:00:00').toLocaleDateString('pt-BR')} <span class="badge text-bg-${situacao.cor}">${situacao.texto}</span>`;
    document.getElementById('resumo-indice-contrato').textContent =
        contrato.indiceReajuste != null ? (ROTULOS_INDICE_REAJUSTE[contrato.indiceReajuste] ?? '—') : '—';
    document.getElementById('resumo-ultimo-contrato').textContent = contrato.dataUltimoReajuste
        ? new Date(contrato.dataUltimoReajuste + 'T00:00:00').toLocaleDateString('pt-BR')
        : '—';
    document.getElementById('resumo-aprovacao-contrato').textContent = avulso ? '—' : aprovacao;

    const pendente = document.getElementById('resumo-pendente-contrato');
    pendente.classList.toggle('d-none', !contrato.reajustePendenteId);
    if (contrato.reajustePendenteId) {
        pendente.innerHTML =
            `<strong>Reajuste aguardando aprovação</strong>` +
            `<span>Aniversário em ${new Date(contrato.reajustePendenteVigencia + 'T00:00:00').toLocaleDateString('pt-BR')}.</span>` +
            `<button type="button" class="btn btn-sm btn-outline-warning" data-id="${contrato.id}" data-descricao="${escaparHtml(contrato.descricao)}" id="btn-resumo-decidir-reajuste">Decidir agora</button>`;
        document.getElementById('btn-resumo-decidir-reajuste').addEventListener('click', () => {
            // Abre o Histórico só depois que este modal terminou de fechar
            // (dois modais do Bootstrap ao mesmo tempo quebram o fundo escuro).
            document.getElementById('modal-contrato').addEventListener('hidden.bs.modal',
                () => abrirModalHistorico(contrato.id, contrato.descricao), { once: true });
            modalContrato.hide();
        });
    }

    resumo.classList.remove('d-none');
}

async function abrirModalNovoContrato() {
    limparFormularioContrato();
    renderizarResumoContrato(null);
    document.getElementById('cliente-modal-contrato').textContent = 'Novo contrato';
    document.getElementById('titulo-modal-contrato').textContent = 'Novo contrato';
    document.getElementById('dataInicioContrato').value = dataLocalIso();

    try {
        await definirMunicipioNoSelect('localPrestacaoContrato', null);
        await carregarServicosContrato();
        adicionarLinhaServico();
        modalContrato.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function abrirModalEditarContrato(id) {
    limparFormularioContrato();
    renderizarResumoContrato(null);
    document.getElementById('titulo-modal-contrato').textContent = 'Editar contrato';

    try {
        const contrato = await apiFetch(`/api/contratos/${id}`);
        document.getElementById('cliente-modal-contrato').textContent = contrato.clienteNome;
        document.getElementById('titulo-modal-contrato').textContent = contrato.descricao;
        renderizarResumoContrato(contrato);
        await carregarServicosContrato();
        preencherClienteAtual(contrato.clienteId, contrato.clienteNome);

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
        document.getElementById('reajusteAutomatico').value = contrato.reajusteAutomatico == null ? '' : String(contrato.reajusteAutomatico);
        document.getElementById('aplicarIndiceNegativo').checked = contrato.aplicarIndiceNegativo;
        document.getElementById('observacao').value = contrato.observacao ?? '';
        await definirMunicipioNoSelect('localPrestacaoContrato', contrato.codigoMunicipioPrestacao);
        aplicarRegraTipoCobranca();

        (contrato.servicos ?? []).forEach(s => adicionarLinhaServico(s));

        // Antes o bloco de serviços era ESCONDIDO na edição (a aba ficava
        // vazia). Agora as linhas aparecem e podem ser alteradas: o backend
        // aplica a composição nova, recalcula o valor e registra no
        // Histórico. Cliente continua travado.
        $('#clienteId').prop('disabled', true);
        document.getElementById('bloco-servicos').classList.remove('d-none');
        document.getElementById('nota-servicos-nao-editaveis').textContent =
            'Alterar os serviços muda o valor do contrato e fica registrado no Histórico de valores. Notas já emitidas não mudam.';

        linhasOriginaisContrato = normalizarLinhas(contrato.servicos);
        document.getElementById('mudanca-tipo').value = '3';
        document.getElementById('mudanca-vigencia').value = dataLocalIso();
        document.getElementById('mudanca-motivo').value = '';
        document.getElementById('bloco-mudanca-servicos').classList.remove('d-none');

        document.getElementById('documentos-indisponivel').classList.add('d-none');
        document.getElementById('documentos-disponivel').classList.remove('d-none');
        document.getElementById('historico-indisponivel').classList.add('d-none');
        document.getElementById('tabela-historico-contrato').classList.remove('d-none');
        await Promise.all([carregarDocumentosContrato(id), carregarHistoricoContrato(id)]);

        modalContrato.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function formatarTamanhoArquivo(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

async function carregarDocumentosContrato(contratoId) {
    const corpo = document.getElementById('lista-documentos-contrato');
    try {
        const documentos = await apiFetch(`/api/contratos/${contratoId}/documentos`);

        if (documentos.length === 0) {
            corpo.innerHTML = '<tr><td colspan="4" class="text-center text-muted">Nenhum documento anexado ainda.</td></tr>';
            return;
        }

        corpo.innerHTML = documentos.map(d => `
            <tr>
                <td><a href="/api/contratos/documentos/${d.id}/download" target="_blank">${d.nomeOriginal}</a></td>
                <td>${formatarTamanhoArquivo(d.tamanhoBytes)}</td>
                <td>${new Date(d.createdAt).toLocaleDateString('pt-BR')}</td>
                <td><button type="button" class="btn btn-sm btn-outline-danger btn-excluir-documento" data-id="${d.id}"><i class="bi bi-trash"></i></button></td>
            </tr>
        `).join('');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function enviarDocumentoContrato() {
    const contratoId = document.getElementById('contratoId').value;
    const campoArquivo = document.getElementById('documento-arquivo');
    if (!contratoId || !campoArquivo.files[0]) return;

    const formData = new FormData();
    formData.append('arquivo', campoArquivo.files[0]);

    try {
        // Upload multipart — não usa apiFetch (que sempre manda
        // Content-Type: application/json); o navegador define o
        // boundary do multipart/form-data automaticamente. Mesmo padrão
        // já usado em empresas.js pro upload de certificado.
        const resposta = await fetch(`/api/contratos/${contratoId}/documentos`, {
            method: 'POST',
            credentials: 'same-origin',
            headers: obterCsrfHeader(),
            body: formData
        });

        if (!resposta.ok) {
            const corpo = await resposta.json().catch(() => null);
            throw new Error(extrairMensagemDeErro(corpo) ?? `Erro ${resposta.status} ao anexar o documento.`);
        }

        campoArquivo.value = '';
        await carregarDocumentosContrato(contratoId);
        mostrarToast('Documento anexado.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function excluirDocumentoContrato(documentoId) {
    const confirmado = await confirmarAcao('Excluir este documento?', { titulo: 'Excluir documento', textoBotao: 'Excluir', variante: 'perigo' });
    if (!confirmado) return;

    const contratoId = document.getElementById('contratoId').value;
    try {
        await apiFetch(`/api/contratos/documentos/${documentoId}`, { method: 'DELETE' });
        await carregarDocumentosContrato(contratoId);
        mostrarToast('Documento excluído.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function carregarHistoricoContrato(contratoId) {
    const corpo = document.querySelector('#tabela-historico-contrato tbody');
    try {
        const itens = await apiFetch(`/api/contratos/${contratoId}/historico`);

        if (itens.length === 0) {
            corpo.innerHTML = '<tr><td colspan="3" class="text-center text-muted">Nenhum evento registrado ainda.</td></tr>';
            return;
        }

        corpo.innerHTML = itens.map(i => `
            <tr>
                <td>${new Date(i.data).toLocaleString('pt-BR')}</td>
                <td>${i.origem}</td>
                <td>${i.descricao}</td>
            </tr>
        `).join('');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function salvarContrato(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-contrato');

    if (!validarFormularioComAbas(document.getElementById('form-contrato'), '.tab-conteudo-contrato', trocarAbaContrato, 'erro-contrato')) {
        return;
    }

    const id = document.getElementById('contratoId').value;
    const valorIndice = document.getElementById('indiceReajuste').value;
    const indiceReajuste = valorIndice !== '' ? parseInt(valorIndice, 10) : null;
    const diasAlertaOverride = document.getElementById('diasAlertaOverride').value;
    const observacao = document.getElementById('observacao').value.trim() || null;

    const linhasServico = coletarLinhasServico();

    // Vale pra criar E editar (as linhas vão nos dois). Guid vazio ("")
    // quebra o model binding no backend antes de a validação do
    // FluentValidation rodar (vira 400 silencioso, sem log e sem mensagem
    // por campo) — barra aqui, na aba onde o problema está, com uma
    // mensagem que a pessoa entende.
    if (linhasServico.length === 0 || linhasServico.some(l => !l.servicoId)) {
        trocarAbaContrato('tab-servico');
        mostrarErroFormulario('erro-contrato', 'Selecione o Serviço em todas as linhas da aba "Serviço" antes de salvar.');
        return;
    }

    const idsUnicos = new Set(linhasServico.map(l => l.servicoId));
    if (idsUnicos.size !== linhasServico.length) {
        trocarAbaContrato('tab-servico');
        mostrarErroFormulario('erro-contrato', 'O mesmo serviço não pode aparecer em mais de uma linha.');
        return;
    }

    try {
        if (id) {
            const payload = {
                descricao: document.getElementById('descricao').value,
                dataInicioContrato: document.getElementById('dataInicioContrato').value,
                periodicidadeReajusteMeses: parseInt(document.getElementById('periodicidadeReajusteMeses').value, 10),
                indiceReajuste: indiceReajuste,
                diasAlertaOverride: diasAlertaOverride ? parseInt(diasAlertaOverride, 10) : null,
                observacao: observacao,
                status: parseInt(document.getElementById('status').value, 10),
                dataFim: document.getElementById('dataFim').value || null,
                tipoCobranca: parseInt(document.getElementById('tipoCobranca').value, 10),
                permitirAlterarValorNaEmissao: document.getElementById('permitirAlterarValorNaEmissao').checked,
                // C2: "" = município da empresa.
                codigoMunicipioPrestacao: $('#localPrestacaoContrato').val() || '',
                servicos: linhasServico,
                ...lerOpcoesReajusteAutomatico()
            };

            // Composição mudou → vira evento do Histórico de valores:
            // precisa do tipo, da vigência e (na alteração de escopo) do motivo.
            if (normalizarLinhas(linhasServico) !== linhasOriginaisContrato) {
                const tipo = parseInt(document.getElementById('mudanca-tipo').value, 10);
                const vigencia = document.getElementById('mudanca-vigencia').value;
                const motivo = document.getElementById('mudanca-motivo').value.trim();

                if (!vigencia || (tipo === 3 && !motivo)) {
                    trocarAbaContrato('tab-servico');
                    mostrarErroFormulario('erro-contrato', tipo === 3
                        ? 'Você alterou os serviços: informe a data de vigência e o motivo da alteração de escopo.'
                        : 'Você alterou os serviços: informe a data de vigência do reajuste.');
                    return;
                }

                payload.tipoAlteracaoServicos = tipo;
                payload.vigenciaAlteracaoServicos = vigencia;
                payload.motivoAlteracaoServicos = motivo || null;
            }

            await apiFetch(`/api/contratos/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            const payload = {
                empresaId: empresaAtualIdContratos,
                clienteId: document.getElementById('clienteId').value,
                descricao: document.getElementById('descricao').value,
                servicos: linhasServico,
                dataInicioContrato: document.getElementById('dataInicioContrato').value,
                periodicidadeReajusteMeses: parseInt(document.getElementById('periodicidadeReajusteMeses').value, 10),
                indiceReajuste: indiceReajuste,
                diasAlertaOverride: diasAlertaOverride ? parseInt(diasAlertaOverride, 10) : null,
                observacao: observacao,
                status: parseInt(document.getElementById('status').value, 10),
                dataFim: document.getElementById('dataFim').value || null,
                tipoCobranca: parseInt(document.getElementById('tipoCobranca').value, 10),
                permitirAlterarValorNaEmissao: document.getElementById('permitirAlterarValorNaEmissao').checked,
                // C2: "" = município da empresa.
                codigoMunicipioPrestacao: $('#localPrestacaoContrato').val() || '',
                ...lerOpcoesReajusteAutomatico()
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
    document.getElementById('titulo-modal-historico').textContent = `Histórico de valores — ${descricao}`;
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

// TipoMovimentacaoValor / StatusMovimentacaoValor (Domain.Enums) como número.
const ROTULOS_TIPO_MOVIMENTACAO = {
    1: { texto: 'Valor inicial', classe: 'text-bg-secondary' },
    2: { texto: 'Reajuste', classe: 'text-bg-primary' },
    3: { texto: 'Alteração de escopo', classe: 'text-bg-info' }
};
const ROTULOS_STATUS_MOVIMENTACAO = { 2: 'Aguardando aprovação', 3: 'Não aplicado' };

function formatarReais(valor) {
    return valor == null ? '—' : Number(valor).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

// Histórico de valores: um evento por linha + os serviços cobrados a
// partir dele. Tudo que vem do cadastro (motivo, nome do serviço) passa
// por escaparHtml — é texto digitado por usuário.
async function carregarHistorico(contratoId) {
    const corpo = document.querySelector('#tabela-historico-reajuste tbody');

    try {
        const eventos = await apiFetch(`/api/contratos/${contratoId}/historico-valores`);
        renderizarPainelReajustePendente(eventos.find(e => e.status === 2) ?? null);

        if (eventos.length === 0) {
            corpo.innerHTML = '<tr><td colspan="7" class="text-center text-muted">Nenhum valor registrado ainda.</td></tr>';
            return;
        }

        corpo.innerHTML = eventos.map(e => {
            const tipo = ROTULOS_TIPO_MOVIMENTACAO[e.tipo] ?? { texto: 'Evento', classe: 'text-bg-light' };
            const status = ROTULOS_STATUS_MOVIMENTACAO[e.status];
            const composicao = (e.composicao ?? [])
                .map(l => `${escaparHtml(l.servicoDescricao)}: ${Number(l.quantidade).toLocaleString('pt-BR')} × ${formatarReais(l.valorUnitario)}`)
                .join(' · ');

            return `
                <tr>
                    <td>${new Date(e.dataVigencia + 'T00:00:00').toLocaleDateString('pt-BR')}</td>
                    <td><span class="badge ${tipo.classe}">${tipo.texto}</span>${status ? `<div class="small text-warning">${status}</div>` : ''}</td>
                    <td class="text-end">${formatarReais(e.valorAnterior)}</td>
                    <td class="text-end">${formatarReais(e.valorNovo)}</td>
                    <td class="text-end">${(e.percentualAplicado ?? e.percentualCalculado) != null ? Number(e.percentualAplicado ?? e.percentualCalculado).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%' : '—'}${e.automatico ? '<div class="small text-muted">automático</div>' : ''}</td>
                    <td>${e.indice != null ? (ROTULOS_INDICE_REAJUSTE[e.indice] ?? '—') : '—'}</td>
                    <td>${e.motivo ? escaparHtml(e.motivo) : '—'}</td>
                </tr>
                ${composicao ? `<tr class="table-light"><td></td><td colspan="6" class="small text-muted pt-0">${composicao}</td></tr>` : ''}`;
        }).join('');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function registrarReajuste(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-reajuste');

    const contratoId = document.getElementById('reajuste-contratoId').value;
    const valorIndice = document.getElementById('reajuste-indiceUsado').value;
    const payload = {
        dataReajuste: document.getElementById('reajuste-dataReajuste').value,
        valorNovo: parseFloat(document.getElementById('reajuste-valorNovo').value),
        indiceUsado: valorIndice !== '' ? parseInt(valorIndice, 10) : null,
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

// ---------------------------------------------------------------------
// Reajuste do aniversário (índice): opções do contrato, verificação sob
// demanda e decisão do reajuste "aguardando aprovação".
// ---------------------------------------------------------------------

function lerOpcoesReajusteAutomatico() {
    const valor = document.getElementById('reajusteAutomatico').value;
    return {
        reajusteAutomatico: valor === '' ? null : valor === 'true',
        aplicarIndiceNegativo: document.getElementById('aplicarIndiceNegativo').checked
    };
}

function formatarPercentualContrato(valor) {
    return Number(valor).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 4 }) + '%';
}

function formatarCompetenciaContrato(iso) {
    const [ano, mes] = String(iso).split('-');
    return `${mes}/${ano}`;
}

function formatarDataContrato(iso) {
    return new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');
}

// Percentual que "Aplicar" usaria: o calculado, ou 0% se for negativo e o
// contrato não permitir reduzir (mesma regra do servidor).
function percentualEfetivoPendente(evento) {
    const calculado = Number(evento.percentualCalculado ?? 0);
    return calculado < 0 && !contratoAtualParaHistorico?.aplicarIndiceNegativo ? 0 : calculado;
}

function renderizarPainelReajustePendente(evento) {
    reajustePendenteAtual = evento;
    const painel = document.getElementById('painel-reajuste-pendente');
    const formManual = document.getElementById('form-reajuste');

    ocultarErroFormulario('erro-decisao-reajuste');
    document.getElementById('bloco-negociar').classList.add('d-none');
    document.getElementById('bloco-recusar').classList.add('d-none');

    painel.classList.toggle('d-none', !evento);
    // Com um reajuste pendente, registrar outro manualmente é bloqueado no servidor.
    formManual.classList.toggle('d-none', !!evento);
    if (!evento) return;

    const valorAtual = contratoAtualParaHistorico?.valorAtual ?? evento.valorAnterior;
    const percentual = percentualEfetivoPendente(evento);
    const valorNovo = Math.round(valorAtual * (1 + percentual / 100) * 100) / 100;
    const nomeIndice = ROTULOS_INDICE_REAJUSTE[evento.indice] ?? 'Índice';

    document.getElementById('pendente-resumo').innerHTML =
        `Aniversário em <strong>${formatarDataContrato(evento.dataVigencia)}</strong>: ` +
        `${nomeIndice} acumulado <strong>${formatarPercentualContrato(evento.percentualCalculado)}</strong> — ` +
        `${formatarReais(valorAtual)} → <strong>${formatarReais(valorNovo)}</strong>` +
        (percentual !== Number(evento.percentualCalculado) ? ' <span class="text-muted">(índice negativo: valor mantido, conforme o contrato)</span>' : '');

    document.getElementById('pendente-janela').textContent = evento.competenciaIndiceInicio
        ? `Meses do índice: ${formatarCompetenciaContrato(evento.competenciaIndiceInicio)} a ${formatarCompetenciaContrato(evento.competenciaIndiceFim)} (12 últimos publicados até o aniversário).`
        : '';
    document.getElementById('pendente-percentual-botao').textContent = formatarPercentualContrato(percentual);

    document.getElementById('negociado-percentual').value = '';
    document.getElementById('negociado-valor').value = '';
    document.getElementById('negociado-motivo').value = '';
    document.getElementById('recusa-motivo').value = '';
}

function configurarPainelReajustePendente() {
    const campoPercentual = document.getElementById('negociado-percentual');
    const campoValor = document.getElementById('negociado-valor');
    const base = () => contratoAtualParaHistorico?.valorAtual ?? 0;

    // % e valor se calculam um a partir do outro; o servidor recebe só o que foi digitado por último.
    let ultimoDigitado = 'percentual';
    campoPercentual.addEventListener('input', () => {
        ultimoDigitado = 'percentual';
        if (campoPercentual.value === '') return;
        campoValor.value = (base() * (1 + parseFloat(campoPercentual.value) / 100)).toFixed(2);
    });
    campoValor.addEventListener('input', () => {
        ultimoDigitado = 'valor';
        if (campoValor.value === '' || !base()) return;
        campoPercentual.value = ((parseFloat(campoValor.value) / base() - 1) * 100).toFixed(2);
    });

    document.getElementById('btn-mostrar-negociar').addEventListener('click', () => {
        document.getElementById('bloco-recusar').classList.add('d-none');
        document.getElementById('bloco-negociar').classList.toggle('d-none');
        campoPercentual.focus();
    });
    document.getElementById('btn-mostrar-recusar').addEventListener('click', () => {
        document.getElementById('bloco-negociar').classList.add('d-none');
        document.getElementById('bloco-recusar').classList.toggle('d-none');
        document.getElementById('recusa-motivo').focus();
    });

    document.getElementById('btn-aplicar-calculado').addEventListener('click', e =>
        executarComBotaoDesabilitado(e.currentTarget, () => decidirReajuste({ acao: 1 })));

    document.getElementById('btn-confirmar-negociado').addEventListener('click', e =>
        executarComBotaoDesabilitado(e.currentTarget, () => {
            const percentual = campoPercentual.value === '' ? null : parseFloat(campoPercentual.value);
            const valor = campoValor.value === '' ? null : parseFloat(campoValor.value);
            return decidirReajuste({
                acao: 2,
                percentualNegociado: ultimoDigitado === 'percentual' ? percentual : null,
                valorNovoNegociado: ultimoDigitado === 'valor' ? valor : null,
                motivo: document.getElementById('negociado-motivo').value.trim() || null
            });
        }));

    document.getElementById('btn-confirmar-recusa').addEventListener('click', e =>
        executarComBotaoDesabilitado(e.currentTarget, () => decidirReajuste({
            acao: 3,
            motivo: document.getElementById('recusa-motivo').value.trim() || null
        })));
}

async function decidirReajuste(corpo) {
    if (!reajustePendenteAtual || !contratoAtualParaHistorico) return;
    ocultarErroFormulario('erro-decisao-reajuste');

    const contratoId = contratoAtualParaHistorico.id;
    try {
        await apiFetch(`/api/contratos/${contratoId}/reajustes/${reajustePendenteAtual.id}/decisao`, {
            method: 'POST',
            body: JSON.stringify({ percentualNegociado: null, valorNovoNegociado: null, motivo: null, ...corpo })
        });

        contratoAtualParaHistorico = await apiFetch(`/api/contratos/${contratoId}`);
        await carregarHistorico(contratoId);
        await carregarContratos();
        mostrarToast(corpo.acao === 3 ? 'Reajuste não aplicado neste ciclo.' : 'Reajuste aplicado.');
    } catch (err) {
        mostrarErroFormulario('erro-decisao-reajuste', err.message);
    }
}

const ROTULOS_RESULTADO_REAJUSTE = {
    1: 'aguardando sua aprovação',
    2: 'aplicado automaticamente',
    3: 'aguardando o índice',
    4: 'atrasado — registre manualmente'
};

async function verificarReajustes() {
    const botao = document.getElementById('btn-verificar-reajustes');
    await executarComBotaoDesabilitado(botao, async function () {
        try {
            const r = await apiFetch(`/api/contratos/reajustes/verificar?empresaId=${empresaAtualIdContratos}`, { method: 'POST' });
            const itens = r.itens ?? [];

            if (itens.length === 0) {
                mostrarQuadroResultado('resultado-verificar-reajustes',
                    'Nenhum contrato com aniversário de reajuste pendente de cálculo.', 'success');
                return;
            }

            const html = itens.map(i => {
                const valores = i.valorNovo != null
                    ? ` — ${formatarPercentualContrato(i.percentualCalculado)}: ${formatarReais(i.valorAnterior)} → ${formatarReais(i.valorNovo)}`
                    : '';
                const detalhe = i.detalhe ? `<div class="small text-muted">${escaparHtml(i.detalhe)}</div>` : '';
                return `<div class="mb-1"><strong>${escaparHtml(i.descricao)}</strong> (aniversário ${formatarDataContrato(i.aniversario)}, ` +
                    `${ROTULOS_INDICE_REAJUSTE[i.indice] ?? ''}): ${ROTULOS_RESULTADO_REAJUSTE[i.resultado] ?? i.resultado}${valores}${detalhe}</div>`;
            }).join('');

            // Algo para o usuário agir (aprovar, índice faltando, atrasado) → não some sozinho.
            const precisaAcao = itens.some(i => i.resultado !== 2);
            mostrarQuadroResultado('resultado-verificar-reajustes', html, precisaAcao ? 'warning' : 'success');
            await carregarContratos();
        } catch (err) {
            mostrarErro(err.message);
        }
    });
}
