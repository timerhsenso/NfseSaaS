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
// Simulação do reajuste pelo índice (modal Histórico de valores).
let simulacaoReajuste = null;          // última resposta da simulação
// Histórico de valores carregado no modal (aviso de reajuste recente e "Desfazer").
let eventosHistoricoAtual = [];
// Último campo mexido no form de reajuste ('valor', 'percentual' ou 'sugestao'):
// define de onde vem o % ao substituir um reajuste recente.
let ultimoCampoReajuste = null;
// Escolha feita no aviso de reajuste recente, usada no envio seguinte.
let decisaoReajusteRecente = null;
let aplicarNegativoEscolhido = null;   // null = regra do contrato; true/false = escolha na tela
let sequenciaSimulacao = 0;            // descarta resposta atrasada
let catalogoServicosContratos = []; // cache do combo, reaproveitado nas linhas dinâmicas

document.addEventListener('DOMContentLoaded', function () {
    const colunas = [
        { data: 'clienteNome' },
        {
            // Número (ordena/pesquisa por ele) + descrição opcional embaixo.
            data: 'numero',
            render: (numero, type, contrato) => {
                if (type !== 'display') return `${numero ?? ''} ${contrato.descricao ?? ''}`.trim();
                const desc = contrato.descricao ? `<div class="small text-muted">${escaparHtml(contrato.descricao)}</div>` : '';
                return `<span class="fw-semibold text-nowrap">${escaparHtml(numero ?? '—')}</span>${desc}`;
            }
        },
        {
            data: 'servicos',
            render: servicos => (servicos ?? []).map(s => s.servicoDescricao).join(', ') || '—'
        },
        { data: 'valorAtual', className: 'col-valor', render: v => Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
        { data: 'tipoCobranca', className: 'col-curta', render: v => ROTULOS_TIPO_COBRANCA[v] ?? '—' },
        {
            data: 'status', className: 'col-curta',
            render: v => {
                const r = ROTULOS_STATUS_CONTRATO[v] ?? { texto: 'Desconhecido', cor: 'secondary' };
                return `<span class="badge text-bg-${r.cor}">${r.texto}</span>`;
            }
        },
        { data: 'dataProximoReajuste', className: 'col-curta', render: v => v ? new Date(v + 'T00:00:00').toLocaleDateString('pt-BR') : '—' },
        {
            data: 'situacao', className: 'col-curta',
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
            render: (data, type, contrato) => acoesDaLinha(
                [
                    { classe: 'btn-editar', icone: 'bi-pencil', texto: 'Editar', cor: 'primary', dados: { id: contrato.id } },
                    { classe: 'btn-historico', icone: 'bi-clock-history', texto: 'Histórico de valores e reajustes', dados: { id: contrato.id, descricao: rotuloContrato(contrato) } }
                ],
                [
                    { classe: 'btn-excluir', icone: 'bi-trash', texto: 'Excluir', perigo: true, dados: { id: contrato.id } }
                ])
        });
    }

    tabelaContratos = new DataTable('#tabela-contratos', {
        columns: colunas,
    });
    tabelaContratos.on('draw', atualizarTotaisContratos);

    configurarExportacao(tabelaContratos, 'Contratos');

    if (podeEditarContrato) {
        modalContrato = new bootstrap.Modal(document.getElementById('modal-contrato'));
        modalHistoricoReajuste = new bootstrap.Modal(document.getElementById('modal-historico-reajuste'));
        document.getElementById('btn-novo-contrato').addEventListener('click', abrirModalNovoContrato);
        document.getElementById('form-contrato').addEventListener('submit', salvarContrato);
        document.getElementById('form-reajuste').addEventListener('submit', registrarReajuste);
        configurarDesfazerReajuste();
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

// Rodapé: receita mensal ativa (contratos Ativos com cobrança Mensal) e
// total do que está na lista agora (respeita a pesquisa da tabela).
function atualizarTotaisContratos() {
    const alvo = document.getElementById('totais-contratos');
    if (!alvo || !tabelaContratos) return;

    const linhas = tabelaContratos.rows({ search: 'applied' }).data().toArray();
    const somar = lista => lista.reduce((t, c) => t + Number(c.valorAtual || 0), 0);
    const ativosMensais = linhas.filter(c => c.status === 1 && c.tipoCobranca === 1);
    const plural = (n, um, varios) => `${n} ${n === 1 ? um : varios}`;

    if (linhas.length === 0) {
        alvo.innerHTML = '';
        return;
    }

    alvo.innerHTML = `
        <div class="text-end">
            <div class="small text-muted">Receita mensal ativa</div>
            <div class="fs-5 fw-semibold text-success">${formatarReais(somar(ativosMensais))}<span class="fs-6 fw-normal text-muted">/mês</span></div>
            <div class="small text-muted">${plural(ativosMensais.length, 'contrato ativo mensal', 'contratos ativos mensais')}</div>
        </div>
        <div class="text-end">
            <div class="small text-muted">Total da lista</div>
            <div class="fs-5 fw-semibold">${formatarReais(somar(linhas))}</div>
            <div class="small text-muted">${plural(linhas.length, 'contrato', 'contratos')}${tabelaContratos.search() ? ' (filtrados)' : ''}</div>
        </div>`;
}

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
                () => abrirModalHistorico(contrato.id, rotuloContrato(contrato)), { once: true });
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
        // Já mostra o número que o contrato vai ter (pode trocar).
        const proximo = await apiFetch(`/api/contratos/proximo-numero?empresaId=${empresaAtualIdContratos}`);
        document.getElementById('numeroContrato').value = proximo.numero;
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
        document.getElementById('titulo-modal-contrato').textContent = rotuloContrato(contrato);
        renderizarResumoContrato(contrato);
        await carregarServicosContrato();
        preencherClienteAtual(contrato.clienteId, contrato.clienteNome);

        document.getElementById('contratoId').value = contrato.id;
        document.getElementById('numeroContrato').value = contrato.numero ?? '';
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

    const botao = document.getElementById('btn-upload-documento');
    if (botao.disabled) return;
    try {
        // Multipart com progresso no botão "Anexar" (não usa apiFetch, que manda JSON).
        await enviarArquivoComProgresso(`/api/contratos/${contratoId}/documentos`, formData, { botao });

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
                numero: document.getElementById('numeroContrato').value.trim(),
                descricao: document.getElementById('descricao').value.trim(),
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
                numero: document.getElementById('numeroContrato').value.trim(),
                descricao: document.getElementById('descricao').value.trim(),
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
    simulacaoReajuste = null;
    aplicarNegativoEscolhido = null;
    ocultarErroFormulario('erro-reajuste');
    document.getElementById('titulo-modal-historico').textContent = `Histórico de valores — ${descricao}`;
    limparFormularioReajuste(contratoId);

    try {
        const contrato = await apiFetch(`/api/contratos/${contratoId}`);
        contratoAtualParaHistorico = contrato;

        // % e valor novo se calculam um a partir do outro, sempre sobre a
        // parte FIXA do contrato (serviço variável fica fora do reajuste —
        // mesma regra do servidor).
        const campoValorNovo = document.getElementById('reajuste-valorNovo');
        const campoPercentual = document.getElementById('reajuste-percentual');
        campoValorNovo.oninput = function () {
            ultimoCampoReajuste = 'valor';
            esconderAvisoReajusteRecente();
            marcarSugestaoComoNaoUsada();
            const { atual, fixa } = partesReajuste();
            if (!campoValorNovo.value || !fixa) return;
            campoPercentual.value = ((parseFloat(campoValorNovo.value) - atual) / fixa * 100).toFixed(2);
        };
        campoPercentual.oninput = function () {
            ultimoCampoReajuste = 'percentual';
            esconderAvisoReajusteRecente();
            marcarSugestaoComoNaoUsada();
            if (!campoPercentual.value) return;
            campoValorNovo.value = valorNovoComPercentual(parseFloat(campoPercentual.value)).toFixed(2);
        };

        document.getElementById('reajuste-dataReajuste').onchange = () => {
            esconderAvisoReajusteRecente();
            simularReajuste();
        };
        document.getElementById('reajuste-indiceUsado').onchange = () => {
            aplicarNegativoEscolhido = null;
            simularReajuste();
        };

        document.getElementById('reajuste-indiceUsado').value = contrato.indiceReajuste ?? '';

        // Primeiro a simulação (traz a data sugerida e a parte variável, que
        // o painel do reajuste pendente também usa), depois o histórico.
        await simularReajuste({ inicial: true });
        await carregarHistorico(contratoId);
        modalHistoricoReajuste.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function limparFormularioReajuste(contratoId) {
    document.getElementById('form-reajuste').reset();
    document.getElementById('reajuste-contratoId').value = contratoId;
    document.getElementById('reajuste-dataReajuste').value = dataLocalIso();
    document.getElementById('reajuste-sugestao').innerHTML = '';
    document.getElementById('reajuste-dica-data').innerHTML = '';
    document.getElementById('reajuste-dica-variavel').classList.add('d-none');
    ultimoCampoReajuste = null;
    esconderAvisoReajusteRecente();
    fecharPainelDesfazer();
    const observacao = document.getElementById('reajuste-observacao');
    delete observacao.dataset.automatica;
    // Texto digitado pela pessoa não é sobrescrito por "Usar este reajuste".
    observacao.oninput = () => { observacao.dataset.automatica = 'false'; };
}

// Valor atual, parte variável (fora do reajuste) e parte fixa (base do %).
function partesReajuste() {
    const atual = Number(contratoAtualParaHistorico?.valorAtual ?? 0);
    const variavel = Number(simulacaoReajuste?.parteVariavel ?? 0);
    return { atual, variavel, fixa: atual - variavel };
}

// Mesma conta do servidor (ReajusteContrato.CalcularValorNovo).
function valorNovoComPercentual(percentual) {
    const { variavel, fixa } = partesReajuste();
    return variavel + Math.round(fixa * (1 + percentual / 100) * 100) / 100;
}

function formatarMesCurto(iso) {
    const [ano, mes] = String(iso).split('-');
    const nomes = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];
    return `${nomes[parseInt(mes, 10) - 1]}/${ano}`;
}

// Chamada ao abrir (inicial: o servidor escolhe data e índice do contrato)
// e a cada troca de data/índice. Só a resposta mais recente é desenhada.
async function simularReajuste({ inicial = false } = {}) {
    const contratoId = document.getElementById('reajuste-contratoId').value;
    const indice = document.getElementById('reajuste-indiceUsado').value;
    const data = document.getElementById('reajuste-dataReajuste').value;
    const caixa = document.getElementById('reajuste-sugestao');
    const minhaVez = ++sequenciaSimulacao;

    const parametros = new URLSearchParams();
    if (!inicial) {
        if (indice !== '') parametros.set('indice', indice);
        if (data) parametros.set('data', data);
    }
    if (aplicarNegativoEscolhido !== null) parametros.set('aplicarNegativo', aplicarNegativoEscolhido);

    caixa.innerHTML = '<div class="small text-muted"><span class="spinner-border spinner-border-sm me-1"></span>Calculando pelo índice…</div>';

    try {
        const r = await apiFetch(`/api/contratos/${contratoId}/reajustes/simulacao?${parametros}`);
        if (minhaVez !== sequenciaSimulacao) return;
        simulacaoReajuste = r;

        if (inicial) {
            document.getElementById('reajuste-dataReajuste').value = r.dataSugerida;
            if (r.indice != null) document.getElementById('reajuste-indiceUsado').value = String(r.indice);
        }

        // "Não definido" escolhido na tela: o servidor usou o índice do
        // contrato, mas a pessoa quer sem índice — não sugere nada.
        if (!inicial && indice === '') {
            renderizarSugestaoReajuste({ ...r, disponivel: false, mensagem: 'Escolha o índice para calcular o reajuste, ou informe o valor novo.' });
        } else {
            renderizarSugestaoReajuste(r);
        }
        renderizarDicasReajuste(r);
    } catch (err) {
        if (minhaVez !== sequenciaSimulacao) return;
        caixa.innerHTML = `<div class="small text-danger">Não foi possível calcular pelo índice: ${escaparHtml(err.message)}</div>`;
    }
}

function renderizarDicasReajuste(r) {
    const dicaData = document.getElementById('reajuste-dica-data');
    const dataTela = document.getElementById('reajuste-dataReajuste').value;
    if (r.aniversario && dataTela === r.aniversario) {
        dicaData.innerHTML = '<i class="bi bi-calendar-check me-1"></i>Aniversário do contrato';
    } else if (r.aniversario) {
        dicaData.innerHTML = `Aniversário: <a href="#" id="link-usar-aniversario">${formatarDataContrato(r.aniversario)}</a>`;
        document.getElementById('link-usar-aniversario').onclick = ev => {
            ev.preventDefault();
            document.getElementById('reajuste-dataReajuste').value = r.aniversario;
            simularReajuste();
        };
    } else {
        dicaData.innerHTML = '';
    }

    const dicaVariavel = document.getElementById('reajuste-dica-variavel');
    const variavel = Number(r.parteVariavel ?? 0);
    dicaVariavel.classList.toggle('d-none', variavel <= 0);
    if (variavel > 0) {
        const nomes = (r.servicosForaDoReajuste ?? []).map(escaparHtml).join(', ');
        dicaVariavel.innerHTML = `<i class="bi bi-info-circle me-1"></i>Fica fora do reajuste (valor variável, muda a cada nota): ` +
            `<strong>${nomes || 'serviço variável'}</strong> — ${formatarReais(variavel)} mantido. O % vale sobre a parte fixa (${formatarReais(r.valorAtual - variavel)}).`;
    }
}

function renderizarSugestaoReajuste(r) {
    const caixa = document.getElementById('reajuste-sugestao');
    const nomeIndice = r.indice != null ? (ROTULOS_INDICE_REAJUSTE[r.indice] ?? 'Índice') : 'Índice';

    if (!r.disponivel) {
        const linkIndices = r.indice != null && r.indice !== 99 && /falta|desatualizad|publicad/i.test(r.mensagem ?? '')
            ? ' <a href="/Indices" target="_blank" rel="noopener">Abrir Índices econômicos</a>'
            : '';
        caixa.innerHTML = `<div class="alert alert-light border small mb-0"><i class="bi bi-calculator me-1"></i>${escaparHtml(r.mensagem ?? '')}${linkIndices}</div>`;
        return;
    }

    const diferenca = Number(r.valorNovo) - Number(r.valorAtual);
    const sinal = diferenca > 0 ? '+' : diferenca < 0 ? '−' : '';
    const indiceNegativo = Number(r.percentualIndice) < 0;

    let linhaNegativo = '';
    if (r.negativoMantido) {
        const motivo = aplicarNegativoEscolhido === false ? 'por escolha sua' : 'conforme o contrato';
        linhaNegativo = `<div class="small mt-1 text-warning-emphasis"><i class="bi bi-arrow-down-right me-1"></i>` +
            `Acumulado negativo: o valor fica igual (${motivo}). <a href="#" data-negativo="true">Aplicar a redução mesmo assim</a></div>`;
    } else if (indiceNegativo) {
        const motivo = aplicarNegativoEscolhido === true ? 'por escolha sua' : 'o contrato permite reduzir';
        linhaNegativo = `<div class="small mt-1 text-warning-emphasis"><i class="bi bi-arrow-down-right me-1"></i>` +
            `Acumulado negativo: o valor diminui (${motivo}). <a href="#" data-negativo="false">Manter o valor</a></div>`;
    }

    const meses = (r.meses ?? []).map(m =>
        `<tr><td>${formatarMesCurto(m.competencia)}</td><td class="text-end">${formatarPercentualContrato(m.variacao)}</td></tr>`).join('');

    caixa.innerHTML = `
        <div class="border rounded p-3 bg-body-tertiary" id="quadro-sugestao-reajuste">
            <div class="d-flex flex-wrap justify-content-between align-items-start gap-2">
                <div>
                    <div class="small text-muted">${nomeIndice} acumulado em 12 meses · ${formatarMesCurto(r.competenciaInicio)} a ${formatarMesCurto(r.competenciaFim)}</div>
                    <div class="fs-5 fw-semibold">${formatarPercentualContrato(r.percentualIndice)}</div>
                    <div>${formatarReais(r.valorAtual)} → <strong>${formatarReais(r.valorNovo)}</strong>
                        <span class="text-muted">(${sinal}${formatarReais(Math.abs(diferenca))} por mês)</span></div>
                    ${linhaNegativo}
                </div>
                <div class="text-end">
                    <button type="button" class="btn btn-success btn-sm" id="btn-usar-reajuste-indice"><i class="bi bi-check2 me-1"></i>Usar este reajuste</button>
                    <div class="small text-success mt-1 d-none" id="sugestao-usada"><i class="bi bi-check-circle me-1"></i>Preenchido abaixo — confira e registre.</div>
                </div>
            </div>
            <a href="#" class="small" id="link-ver-meses">ver os 12 meses</a>
            <div class="d-none mt-2" id="reajuste-meses">
                <table class="table table-sm small mb-0" style="max-width: 280px">
                    <thead><tr><th>Mês</th><th class="text-end">Variação</th></tr></thead>
                    <tbody>${meses}</tbody>
                </table>
            </div>
        </div>`;

    document.getElementById('btn-usar-reajuste-indice').onclick = () => usarSugestaoReajuste(r, nomeIndice);
    document.getElementById('link-ver-meses').onclick = ev => {
        ev.preventDefault();
        const tabela = document.getElementById('reajuste-meses');
        tabela.classList.toggle('d-none');
        ev.currentTarget.textContent = tabela.classList.contains('d-none') ? 'ver os 12 meses' : 'esconder os meses';
    };
    caixa.querySelectorAll('[data-negativo]').forEach(link => link.onclick = ev => {
        ev.preventDefault();
        aplicarNegativoEscolhido = link.dataset.negativo === 'true';
        simularReajuste();
    });
}

function usarSugestaoReajuste(r, nomeIndice) {
    document.getElementById('reajuste-valorNovo').value = Number(r.valorNovo).toFixed(2);
    document.getElementById('reajuste-percentual').value = Number(r.percentualAplicado);
    ultimoCampoReajuste = 'sugestao';
    esconderAvisoReajusteRecente();

    const observacao = document.getElementById('reajuste-observacao');
    if (!observacao.value.trim() || observacao.dataset.automatica === 'true') {
        observacao.value = `${nomeIndice} acumulado ${formatarPercentualContrato(r.percentualIndice)} ` +
            `(${formatarMesCurto(r.competenciaInicio)} a ${formatarMesCurto(r.competenciaFim)})` +
            (r.negativoMantido ? ' — negativo, valor mantido' : '');
        observacao.dataset.automatica = 'true';
    }

    document.getElementById('sugestao-usada')?.classList.remove('d-none');
    document.querySelector('#form-reajuste button[type="submit"]').focus();
}

// Digitou outro valor/% depois de "Usar": a sugestão continua como referência.
function marcarSugestaoComoNaoUsada() {
    document.getElementById('sugestao-usada')?.classList.add('d-none');
}

// TipoMovimentacaoValor / StatusMovimentacaoValor (Domain.Enums) como número.
const ROTULOS_TIPO_MOVIMENTACAO = {
    1: { texto: 'Valor inicial', classe: 'text-bg-secondary' },
    2: { texto: 'Reajuste', classe: 'text-bg-primary' },
    3: { texto: 'Alteração de escopo', classe: 'text-bg-info' }
};
const ROTULOS_STATUS_MOVIMENTACAO = { 2: 'Aguardando aprovação', 3: 'Não aplicado' };
const STATUS_APLICADO = 1;
const STATUS_DESFEITO = 4;
const TIPO_REAJUSTE = 2;

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
        eventosHistoricoAtual = eventos;
        renderizarPainelReajustePendente(eventos.find(e => e.status === 2) ?? null);

        if (eventos.length === 0) {
            corpo.innerHTML = '<tr><td colspan="7" class="text-center text-muted">Nenhum valor registrado ainda.</td></tr>';
            return;
        }

        corpo.innerHTML = eventos.map(e => {
            const tipo = ROTULOS_TIPO_MOVIMENTACAO[e.tipo] ?? { texto: 'Evento', classe: 'text-bg-light' };
            const status = ROTULOS_STATUS_MOVIMENTACAO[e.status];
            const desfeito = e.status === STATUS_DESFEITO;
            // Evento desfeito: os serviços dele não valem mais — sem a linha da composição.
            const composicao = desfeito ? '' : (e.composicao ?? [])
                .map(l => `${escaparHtml(l.servicoDescricao)}: ${Number(l.quantidade).toLocaleString('pt-BR')} × ${formatarReais(l.valorUnitario)}`)
                .join(' · ');
            const riscar = valor => desfeito ? `<s>${valor}</s>` : valor;
            const percentual = (e.percentualAplicado ?? e.percentualCalculado) != null
                ? Number(e.percentualAplicado ?? e.percentualCalculado).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%'
                : '—';

            // Só o último reajuste aplicado tem "Desfazer"; se não puder, o
            // clique explica o porquê (no celular não há dica ao passar o mouse).
            const podeMostrarDesfazer = e.podeDesfazer || e.bloqueioDesfazer;
            const botaoDesfazer = podeMostrarDesfazer
                ? `<div><button type="button" class="btn btn-link btn-sm p-0 text-danger btn-desfazer-reajuste" data-id="${e.id}">` +
                  `<i class="bi bi-arrow-counterclockwise"></i> Desfazer</button></div>`
                : '';
            const detalheDesfeito = desfeito
                ? `<div class="small text-danger">Desfeito em ${e.desfeitoEm ? new Date(e.desfeitoEm).toLocaleDateString('pt-BR') : '—'}` +
                  `${e.desfeitoPor ? ` por ${escaparHtml(e.desfeitoPor)}` : ''}${e.motivoDesfazer ? `: ${escaparHtml(e.motivoDesfazer)}` : ''}</div>`
                : '';

            return `
                <tr class="${desfeito ? 'text-muted linha-desfeita' : ''}">
                    <td>${riscar(new Date(e.dataVigencia + 'T00:00:00').toLocaleDateString('pt-BR'))}</td>
                    <td><span class="badge ${desfeito ? 'text-bg-light border' : tipo.classe}">${tipo.texto}</span>` +
                    `${desfeito ? '<div class="small text-danger fw-semibold">Desfeito</div>' : ''}` +
                    `${status ? `<div class="small text-warning">${status}</div>` : ''}${botaoDesfazer}</td>
                    <td class="text-end">${riscar(formatarReais(e.valorAnterior))}</td>
                    <td class="text-end">${riscar(formatarReais(e.valorNovo))}</td>
                    <td class="text-end">${riscar(percentual)}${e.automatico ? '<div class="small text-muted">automático</div>' : ''}</td>
                    <td>${e.indice != null ? (ROTULOS_INDICE_REAJUSTE[e.indice] ?? '—') : '—'}</td>
                    <td>${e.motivo ? escaparHtml(e.motivo) : '—'}${detalheDesfeito}</td>
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

    // Já houve reajuste neste ciclo: antes de gravar, a pessoa escolhe
    // entre substituir o anterior e somar mais um (o servidor exige a escolha).
    const recente = reajusteRecente(payload.dataReajuste);
    if (recente && !decisaoReajusteRecente) {
        mostrarAvisoReajusteRecente(recente, payload);
        return;
    }
    await enviarReajuste(contratoId, payload);
}

async function enviarReajuste(contratoId, payload) {
    const decisao = decisaoReajusteRecente;
    // Cópia: se der erro, a outra opção do aviso ainda usa o valor original.
    const corpo = { ...payload };
    if (decisao?.modo === 'substituir') {
        corpo.valorNovo = decisao.valorNovo;
        // Substituir = o mesmo reajuste, corrigido: fica na data do anterior (o aniversário).
        corpo.dataReajuste = decisao.dataReajuste;
        corpo.substituirReajusteId = decisao.reajusteId;
        corpo.motivoSubstituicao = decisao.motivo;
    } else if (decisao?.modo === 'somar') {
        corpo.confirmarReajusteRecente = true;
    }

    try {
        await apiFetch(`/api/contratos/${contratoId}/reajustes`, { method: 'POST', body: JSON.stringify(corpo) });
        await recarregarAposMudancaDeValor(contratoId);
        mostrarToast(decisao?.modo === 'substituir' ? 'Reajuste anterior desfeito e o novo registrado.' : 'Reajuste registrado com sucesso.');
    } catch (err) {
        mostrarErroFormulario('erro-reajuste', err.message);
        document.getElementById('erro-reajuste').scrollIntoView({ behavior: 'smooth', block: 'center' });
    } finally {
        decisaoReajusteRecente = null;
    }
}

// Valor e aniversário mudaram (reajuste registrado ou desfeito): recarrega
// o contrato, a simulação, o histórico e a lista de contratos.
async function recarregarAposMudancaDeValor(contratoId) {
    contratoAtualParaHistorico = await apiFetch(`/api/contratos/${contratoId}`);
    aplicarNegativoEscolhido = null;
    limparFormularioReajuste(contratoId);
    document.getElementById('reajuste-indiceUsado').value = contratoAtualParaHistorico.indiceReajuste ?? '';
    await simularReajuste({ inicial: true });

    await carregarHistorico(contratoId);
    await carregarContratos(); // valorAtual/situação mudaram na tabela principal também
}

// ---------------------------------------------------------------------
// Dois reajustes no mesmo ciclo e "Desfazer" (RegrasDesfazerReajuste no
// servidor). Reajuste de contrato é anual: um segundo dentro do ciclo
// quase sempre é engano (ex.: registrado antes de sair o índice do mês).
// ---------------------------------------------------------------------

function somarMesesIso(iso, meses) {
    const [ano, mes, dia] = iso.split('-').map(Number);
    const data = new Date(ano, mes - 1 + meses, 1);
    const ultimoDia = new Date(data.getFullYear(), data.getMonth() + 1, 0).getDate();
    data.setDate(Math.min(dia, ultimoDia));
    return dataLocalIso(data);
}

// Mesma regra do servidor (RegrasDesfazerReajuste.ReajusteRecente).
function reajusteRecente(dataNovoIso) {
    if (!dataNovoIso) return null;
    const periodicidade = Number(contratoAtualParaHistorico?.periodicidadeReajusteMeses) > 0
        ? Number(contratoAtualParaHistorico.periodicidadeReajusteMeses) : 12;
    const limite = somarMesesIso(dataNovoIso, -periodicidade);
    return eventosHistoricoAtual
        .filter(e => e.tipo === TIPO_REAJUSTE && e.status === STATUS_APLICADO && e.dataVigencia > limite)
        .sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)))[0] ?? null;
}

// % do reajuste que está sendo registrado: o digitado/sugerido, ou o que o
// valor novo dá sobre a parte fixa atual (se a pessoa digitou o valor).
function percentualDoFormularioReajuste() {
    const campoPercentual = document.getElementById('reajuste-percentual');
    const valorNovo = parseFloat(document.getElementById('reajuste-valorNovo').value);
    if (ultimoCampoReajuste !== 'valor' && campoPercentual.value !== '') return parseFloat(campoPercentual.value);
    const { atual, fixa } = partesReajuste();
    return fixa && !Number.isNaN(valorNovo) ? (valorNovo - atual) / fixa * 100 : null;
}

function esconderAvisoReajusteRecente() {
    const aviso = document.getElementById('aviso-reajuste-recente');
    if (!aviso) return;
    aviso.classList.add('d-none');
    aviso.innerHTML = '';
    document.getElementById('acoes-registrar-reajuste')?.classList.remove('d-none');
    decisaoReajusteRecente = null;
}

function mostrarAvisoReajusteRecente(recente, payload) {
    const aviso = document.getElementById('aviso-reajuste-recente');
    const dias = Math.round((new Date(payload.dataReajuste + 'T00:00:00') - new Date(recente.dataVigencia + 'T00:00:00')) / 86400000);
    const quando = dias <= 0 ? 'na mesma data' : dias === 1 ? 'há 1 dia' : `há ${dias} dias`;
    const nomeIndice = recente.indice != null ? (ROTULOS_INDICE_REAJUSTE[recente.indice] ?? '') : '';
    const pctAnterior = recente.percentualAplicado != null ? ` ${formatarPercentualContrato(recente.percentualAplicado)}` : '';

    // Substituir: o mesmo % sobre o valor de ANTES do reajuste anterior
    // (a parte variável não entra no reajuste — mesma conta do servidor).
    const percentual = percentualDoFormularioReajuste();
    const variavel = Number(simulacaoReajuste?.parteVariavel ?? 0);
    const base = Number(recente.valorAnterior);
    const valorSubstituto = percentual == null || recente.valorAnterior == null
        ? null
        : variavel + Math.round((base - variavel) * (1 + percentual / 100) * 100) / 100;

    const dataAnterior = formatarDataContrato(recente.dataVigencia);
    const opcaoSubstituir = recente.podeDesfazer && valorSubstituto != null && valorSubstituto > 0
        ? `<div class="border rounded bg-body p-2 mb-2">
                <button type="button" class="btn btn-primary btn-sm" id="btn-substituir-reajuste">
                    <i class="bi bi-arrow-repeat me-1"></i>Substituir o reajuste de ${dataAnterior}
                </button>
                <div class="small mt-1">Desfaz o anterior e aplica ${formatarPercentualContrato(percentual)} sobre ${formatarReais(base)} =
                    <strong>${formatarReais(valorSubstituto)}</strong>, na mesma data (${dataAnterior}).</div>
                <label class="form-label small mb-1 mt-2" for="substituir-motivo">Por que substituir? <span class="text-danger">*</span></label>
                <input type="text" maxlength="500" class="form-control form-control-sm" id="substituir-motivo"
                       value="Reajuste refeito com o índice atualizado" />
           </div>`
        : `<div class="small text-muted mb-2"><i class="bi bi-lock me-1"></i>${escaparHtml(recente.bloqueioDesfazer ?? 'O reajuste anterior não pode mais ser substituído.')}</div>`;

    aviso.innerHTML = `
        <div class="mb-2"><i class="bi bi-exclamation-triangle-fill me-1"></i>
            Este contrato já teve reajuste em <strong>${dataAnterior}</strong> (${quando})${nomeIndice || pctAnterior ? `: ${escaparHtml(nomeIndice)}${pctAnterior}` : ''},
            ${formatarReais(recente.valorAnterior)} → ${formatarReais(recente.valorNovo)}. Vai fazer dois reajustes no mesmo ciclo?</div>
        ${opcaoSubstituir}
        <div class="d-flex flex-wrap gap-2 align-items-center">
            <button type="button" class="btn btn-outline-warning btn-sm text-body" id="btn-somar-reajuste">Somar mais um reajuste</button>
            <span class="small">aplica sobre ${formatarReais(contratoAtualParaHistorico?.valorAtual)} = ${formatarReais(payload.valorNovo)}</span>
            <button type="button" class="btn btn-link btn-sm ms-auto" id="btn-cancelar-aviso-reajuste">Cancelar</button>
        </div>`;
    aviso.classList.remove('d-none');
    document.getElementById('acoes-registrar-reajuste').classList.add('d-none');

    const contratoId = document.getElementById('reajuste-contratoId').value;
    document.getElementById('btn-substituir-reajuste')?.addEventListener('click', ev =>
        executarComBotaoDesabilitado(ev.currentTarget, async () => {
            const motivo = document.getElementById('substituir-motivo').value.trim();
            if (motivo.length < 10) {
                mostrarErroFormulario('erro-reajuste', 'Explique por que o reajuste anterior está sendo substituído (mínimo 10 caracteres).');
                document.getElementById('erro-reajuste').scrollIntoView({ behavior: 'smooth', block: 'center' });
                return;
            }
            decisaoReajusteRecente = { modo: 'substituir', reajusteId: recente.id, valorNovo: valorSubstituto, dataReajuste: recente.dataVigencia, motivo };
            await enviarReajuste(contratoId, payload);
        }));
    document.getElementById('btn-somar-reajuste').addEventListener('click', ev =>
        executarComBotaoDesabilitado(ev.currentTarget, async () => {
            decisaoReajusteRecente = { modo: 'somar' };
            await enviarReajuste(contratoId, payload);
        }));
    document.getElementById('btn-cancelar-aviso-reajuste').addEventListener('click', esconderAvisoReajusteRecente);
}

let reajusteParaDesfazer = null;

function configurarDesfazerReajuste() {
    document.querySelector('#tabela-historico-reajuste tbody').addEventListener('click', e => {
        const botao = e.target.closest('.btn-desfazer-reajuste');
        if (botao) abrirPainelDesfazer(botao.dataset.id);
    });
    document.getElementById('btn-cancelar-desfazer').addEventListener('click', fecharPainelDesfazer);
    document.getElementById('btn-confirmar-desfazer').addEventListener('click', e =>
        executarComBotaoDesabilitado(e.currentTarget, desfazerReajuste));
}

function abrirPainelDesfazer(id) {
    const evento = eventosHistoricoAtual.find(e => e.id === id);
    if (!evento) return;
    reajusteParaDesfazer = evento;
    ocultarErroFormulario('erro-desfazer-reajuste');

    const painel = document.getElementById('painel-desfazer-reajuste');
    const pode = !!evento.podeDesfazer;
    document.getElementById('desfazer-resumo').innerHTML = pode
        ? `Reajuste de <strong>${formatarDataContrato(evento.dataVigencia)}</strong> (${formatarReais(evento.valorAnterior)} → ${formatarReais(evento.valorNovo)}). ` +
          `O contrato volta para <strong>${formatarReais(evento.valorAnterior)}</strong>, com os serviços de antes, e o próximo reajuste volta a contar como antes dele. ` +
          `<span class="text-muted">O reajuste fica no histórico como desfeito, com o motivo.</span>`
        : `<i class="bi bi-lock me-1"></i>${escaparHtml(evento.bloqueioDesfazer ?? 'Este reajuste não pode ser desfeito.')}`;
    document.getElementById('desfazer-campos').classList.toggle('d-none', !pode);
    document.getElementById('btn-confirmar-desfazer').classList.toggle('d-none', !pode);
    document.getElementById('btn-cancelar-desfazer').textContent = pode ? 'Cancelar' : 'Fechar';
    document.getElementById('desfazer-motivo').value = '';

    painel.classList.remove('d-none');
    painel.scrollIntoView({ behavior: 'smooth', block: 'center' });
    if (pode) document.getElementById('desfazer-motivo').focus({ preventScroll: true });
}

function fecharPainelDesfazer() {
    reajusteParaDesfazer = null;
    document.getElementById('painel-desfazer-reajuste')?.classList.add('d-none');
}

async function desfazerReajuste() {
    if (!reajusteParaDesfazer || !contratoAtualParaHistorico) return;
    ocultarErroFormulario('erro-desfazer-reajuste');

    const motivo = document.getElementById('desfazer-motivo').value.trim();
    if (motivo.length < 10) {
        mostrarErroFormulario('erro-desfazer-reajuste', 'Explique por que o reajuste está sendo desfeito (mínimo 10 caracteres).');
        return;
    }

    const contratoId = contratoAtualParaHistorico.id;
    try {
        await apiFetch(`/api/contratos/${contratoId}/reajustes/${reajusteParaDesfazer.id}/desfazer`, {
            method: 'POST',
            body: JSON.stringify({ motivo })
        });
        await recarregarAposMudancaDeValor(contratoId);
        mostrarToast('Reajuste desfeito. O valor anterior voltou a valer.');
    } catch (err) {
        mostrarErroFormulario('erro-desfazer-reajuste', err.message);
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
    // Mesma conta do servidor: o % vale só sobre a parte fixa.
    const valorNovo = valorNovoComPercentual(percentual);
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
    // % e valor se calculam um a partir do outro (sobre a parte fixa, como
    // no servidor); o servidor recebe só o que foi digitado por último.
    let ultimoDigitado = 'percentual';
    campoPercentual.addEventListener('input', () => {
        ultimoDigitado = 'percentual';
        if (campoPercentual.value === '') return;
        campoValor.value = valorNovoComPercentual(parseFloat(campoPercentual.value)).toFixed(2);
    });
    campoValor.addEventListener('input', () => {
        ultimoDigitado = 'valor';
        const { atual, fixa } = partesReajuste();
        if (campoValor.value === '' || !fixa) return;
        campoPercentual.value = ((parseFloat(campoValor.value) - atual) / fixa * 100).toFixed(2);
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
