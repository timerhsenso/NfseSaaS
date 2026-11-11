// Plataforma › Planos: catálogo, preços com histórico e vagas de fundador (api/plataforma).
// PublicoPlano (Domain.Cobranca): 1 Empresas, 2 Escritórios.
const GRUPOS_FUNDADOR = { 1: 'Empresas', 2: 'Escritórios' };
const SITUACOES_PRECO = { Vigente: 'success', Agendado: 'info', Anterior: 'secondary' };

let tabelaPlanos;
let planos = [];
let planoEmEdicao = null;   // null = novo plano
let planoDoPreco = null;
let grupoDasVagas = null;
let modalPlano, modalPreco, modalHistorico, modalVagas, modalExcluirPlano, modalReajuste;
let planoDoReajuste = null;
// StatusReajusteAssinatura (Domain.Enums): 1 Agendado, 2 Aplicado, 3 Cancelado, 4 Erro.
const SITUACOES_REAJUSTE = {
    1: { texto: 'Agendado', cor: 'info' },
    2: { texto: 'Aplicado', cor: 'success' },
    3: { texto: 'Cancelado', cor: 'secondary' },
    4: { texto: 'Erro no Asaas', cor: 'danger' }
};
let planoParaExcluir = null;
let relogioExclusaoPlano = null;

function reaisPlano(valor) {
    return valor == null ? '—' : Number(valor).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

function dataPlano(iso) {
    if (!iso) return '—';
    const [a, m, d] = iso.slice(0, 10).split('-');
    return `${d}/${m}/${a}`;
}

// Botão que enviou o formulário (Enter no campo também conta).
function botaoSalvar(e) {
    return e.submitter || e.target.querySelector('[type="submit"]');
}

function numeroOuNulo(id) {
    const v = document.getElementById(id).value.trim();
    return v === '' ? null : Number(v);
}

document.addEventListener('DOMContentLoaded', function () {
    modalPlano = new bootstrap.Modal('#modal-plano');
    modalPreco = new bootstrap.Modal('#modal-preco');
    modalHistorico = new bootstrap.Modal('#modal-historico-precos');
    modalVagas = new bootstrap.Modal('#modal-vagas');
    modalExcluirPlano = new bootstrap.Modal('#modal-excluir-plano');
    modalReajuste = new bootstrap.Modal('#modal-reajuste');

    tabelaPlanos = new DataTable('#tabela-planos', {
        order: [],
        paging: false,
        columns: [
            {
                data: 'nome',
                render: (v, type, p) => type !== 'display' ? `${v} ${p.descricao}` :
                    `<div class="fw-semibold">${escaparHtml(v)} <span class="small text-muted">#${p.codigo}</span></div>` +
                    `<div class="small text-muted">${escaparHtml(p.descricao)}</div>`
            },
            {
                data: null, orderable: false,
                render: p => [
                    p.ofertarParaEmpresa ? '<span class="badge text-bg-light border">Empresas</span>' : '',
                    p.ofertarParaContador ? '<span class="badge text-bg-light border">Contadores</span>' : ''
                ].filter(Boolean).join(' ') + `<div class="small text-muted">fundador: ${GRUPOS_FUNDADOR[p.publico] ?? '—'}</div>`
            },
            {
                data: 'maxEmpresas', className: 'col-curta',
                render: (v, type, p) => type !== 'display' ? v :
                    `${v} empresa(s)<div class="small text-muted">${p.notasPorMes ? p.notasPorMes + ' notas/mês' : 'notas sem limite'}</div>`
            },
            {
                data: 'precoMensal', className: 'text-end col-valor',
                render: (v, type, p) => type !== 'display' ? (v ?? 0) :
                    (v == null ? '<span class="text-danger small">sem preço vigente</span>' :
                        `<strong>${reaisPlano(v)}</strong><div class="small text-muted">fundador ${reaisPlano(p.precoFundador)} · desde ${dataPlano(p.vigenteDesde)}</div>`) +
                    (p.proximoVigenteDesde
                        ? `<div class="small text-info">a partir de ${dataPlano(p.proximoVigenteDesde)}: ${reaisPlano(p.proximoPrecoMensal)} / ${reaisPlano(p.proximoPrecoFundador)}</div>`
                        : '')
            },
            { data: 'assinantes', className: 'text-end col-valor' },
            {
                data: 'aVenda', className: 'col-curta',
                render: (v, type) => type !== 'display' ? (v ? 'À venda' : 'Fora de venda') :
                    (v ? '<span class="badge text-bg-success">À venda</span>' : '<span class="badge text-bg-secondary">Fora de venda</span>')
            },
            {
                data: 'id', orderable: false,
                render: (id, type, p) => acoesDaLinha(
                    [
                        { classe: 'btn-preco-plano', icone: 'bi-currency-dollar', texto: 'Preço', titulo: 'Novo preço', cor: 'primary', mostrarTexto: true, dados: { id } },
                        { classe: 'btn-editar-plano', icone: 'bi-pencil', texto: 'Alterar', dados: { id } }
                    ],
                    [
                        { classe: 'btn-historico-plano', icone: 'bi-clock-history', texto: 'Histórico de preços', dados: { id } },
                        p.assinantes > 0 && { classe: 'btn-reajuste-plano', icone: 'bi-graph-up-arrow', texto: 'Reajustar quem já assina', dados: { id } },
                        { divisor: true },
                        p.podeExcluir && { classe: 'btn-excluir-plano', icone: 'bi-trash', texto: 'Excluir (nunca foi assinado)', perigo: true, dados: { id } }
                    ])
            }
        ]
    });

    document.getElementById('btn-atualizar-planos').addEventListener('click', function () {
        executarComBotaoDesabilitado(this, carregarTudo);
    });
    document.getElementById('btn-novo-plano').addEventListener('click', () => abrirPlano(null));

    document.getElementById('tabela-planos').addEventListener('click', async function (e) {
        const botao = e.target.closest('button');
        if (!botao) return;
        const plano = planos.find(p => p.id === botao.dataset.id);
        if (!plano) return;
        if (botao.classList.contains('btn-editar-plano')) abrirPlano(plano);
        else if (botao.classList.contains('btn-preco-plano')) abrirPreco(plano);
        else if (botao.classList.contains('btn-historico-plano')) await executarComBotaoDesabilitado(botao, () => abrirHistorico(plano));
        else if (botao.classList.contains('btn-reajuste-plano')) await executarComBotaoDesabilitado(botao, () => abrirReajuste(plano));
        else if (botao.classList.contains('btn-excluir-plano')) await executarComBotaoDesabilitado(botao, () => pedirCodigoExclusaoPlano(plano));
    });

    document.getElementById('vagas-fundador').addEventListener('click', function (e) {
        const botao = e.target.closest('.btn-alterar-vagas');
        if (!botao) return;
        grupoDasVagas = Number(botao.dataset.publico);
        document.getElementById('vagas-grupo').textContent = GRUPOS_FUNDADOR[grupoDasVagas];
        document.getElementById('vagas-usadas').textContent = botao.dataset.usadas;
        document.getElementById('vagas-quantidade').value = botao.dataset.quantidade;
        document.getElementById('vagas-campanha-ate').value = botao.dataset.campanhaAte || '';
        document.getElementById('vagas-motivo').value = '';
        ocultarErroFormulario('erro-vagas');
        modalVagas.show();
    });

    document.getElementById('form-plano').addEventListener('submit', salvarPlano);
    document.getElementById('form-preco').addEventListener('submit', salvarPreco);
    document.getElementById('form-vagas').addEventListener('submit', salvarVagas);
    document.getElementById('form-excluir-plano').addEventListener('submit', confirmarExclusaoPlano);
    document.getElementById('btn-previa-reajuste').addEventListener('click', function () {
        executarComBotaoDesabilitado(this, carregarPreviaReajuste);
    });
    document.getElementById('btn-agendar-reajuste').addEventListener('click', function () {
        executarComBotaoDesabilitado(this, agendarReajuste);
    });
    // Mudou data ou motivo: a prévia anterior não vale mais para agendar.
    ['reajuste-data', 'reajuste-motivo'].forEach(id => document.getElementById(id).addEventListener('input', () => {
        document.getElementById('btn-agendar-reajuste').disabled = true;
    }));
    document.getElementById('corpo-reajustes').addEventListener('click', function (e) {
        const botao = e.target.closest('.btn-cancelar-lote');
        if (botao) executarComBotaoDesabilitado(botao, () => cancelarLoteReajuste(botao.dataset.lote));
    });
    document.getElementById('modal-excluir-plano').addEventListener('hidden.bs.modal', pararRelogioExclusaoPlano);

    carregarTudo();
});

async function carregarTudo() {
    await Promise.all([carregarPlanos(), carregarVagas()]);
}

async function carregarPlanos() {
    try {
        planos = await apiFetch('/api/plataforma/planos');
        tabelaPlanos.clear().rows.add(planos).draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

// DateOnly ("2027-01-31") sem passar por Date (evita mostrar o dia anterior).
function diaBrVagas(iso) {
    const [ano, mes, dia] = iso.split('-');
    return `${dia}/${mes}/${ano}`;
}

async function carregarVagas() {
    try {
        const vagas = await apiFetch('/api/plataforma/vagas-fundador');
        document.getElementById('vagas-fundador').innerHTML = vagas.map(v => `
            <div class="col-md-6">
                <div class="border rounded-3 p-3 d-flex justify-content-between align-items-center">
                    <div>
                        <div class="fw-semibold">${GRUPOS_FUNDADOR[v.publico] ?? v.publico}</div>
                        <div class="small text-muted">${v.usadas} de ${v.quantidade} usada(s), ${v.reservas} reservada(s) sem assinar ·
                            <span class="${v.restantes > 0 ? 'text-success' : 'text-danger'} fw-semibold">${v.restantes} sobrando</span></div>
                        <div class="small ${v.campanhaAberta ? 'text-success' : 'text-muted'}">
                            ${v.campanhaAberta ? 'Campanha aberta' : 'Campanha encerrada'}${v.campanhaAte ? ` · até ${diaBrVagas(v.campanhaAte)}` : ' · sem data-fim'}
                        </div>
                    </div>
                    <button type="button" class="btn btn-outline-secondary btn-sm btn-alterar-vagas"
                            data-publico="${v.publico}" data-quantidade="${v.quantidade}" data-usadas="${v.usadas}" data-campanha-ate="${v.campanhaAte ?? ''}">
                        <i class="bi bi-pencil"></i> Alterar
                    </button>
                </div>
            </div>`).join('');
    } catch (err) {
        mostrarErro(err.message);
    }
}

function abrirPlano(plano) {
    planoEmEdicao = plano;
    document.getElementById('titulo-modal-plano').textContent = plano ? `Alterar plano ${plano.nome}` : 'Novo plano';
    document.getElementById('plano-nome').value = plano?.nome ?? '';
    document.getElementById('plano-descricao').value = plano?.descricao ?? '';
    document.getElementById('plano-ordem').value = plano?.ordem ?? 100;
    document.getElementById('plano-avenda').checked = plano?.aVenda ?? true;
    document.getElementById('plano-para-empresa').checked = plano?.ofertarParaEmpresa ?? false;
    document.getElementById('plano-para-contador').checked = plano?.ofertarParaContador ?? true;
    document.getElementById('plano-publico').value = String(plano?.publico ?? 2);
    document.getElementById('plano-publico').disabled = !!plano && plano.assinantes > 0;
    document.getElementById('plano-max-empresas').value = plano?.maxEmpresas ?? '';
    document.getElementById('plano-notas').value = plano?.notasPorMes ?? '';
    document.getElementById('plano-preco').value = '';
    document.getElementById('plano-preco-fundador').value = '';
    // Preço só na criação; depois, pelo botão "Preço" (fica no histórico).
    document.querySelectorAll('.grupo-preco-inicial').forEach(el => el.classList.toggle('d-none', !!plano));
    ocultarErroFormulario('erro-plano');
    modalPlano.show();
}

async function salvarPlano(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-plano');
    const corpo = {
        nome: document.getElementById('plano-nome').value.trim(),
        descricao: document.getElementById('plano-descricao').value.trim(),
        publico: Number(document.getElementById('plano-publico').value),
        ofertarParaEmpresa: document.getElementById('plano-para-empresa').checked,
        ofertarParaContador: document.getElementById('plano-para-contador').checked,
        maxEmpresas: numeroOuNulo('plano-max-empresas') ?? 0,
        notasPorMes: numeroOuNulo('plano-notas'),
        aVenda: document.getElementById('plano-avenda').checked,
        ordem: numeroOuNulo('plano-ordem') ?? 0,
        precoMensal: planoEmEdicao ? null : numeroOuNulo('plano-preco'),
        precoFundador: planoEmEdicao ? null : numeroOuNulo('plano-preco-fundador')
    };

    if (planoEmEdicao && planoEmEdicao.aVenda && !corpo.aVenda && planoEmEdicao.assinantes > 0
        && !await confirmarAcao(`Tirar ${planoEmEdicao.nome} de venda? Ele some da tela Assinatura; os ${planoEmEdicao.assinantes} assinante(s) atuais continuam no plano.`,
            { titulo: 'Tirar de venda', textoBotao: 'Tirar de venda' })) return;

    await executarComBotaoDesabilitado(botaoSalvar(e), async function () {
        try {
            await apiFetch(planoEmEdicao ? `/api/plataforma/planos/${planoEmEdicao.id}` : '/api/plataforma/planos',
                { method: planoEmEdicao ? 'PUT' : 'POST', body: JSON.stringify(corpo) });
            modalPlano.hide();
            mostrarToast(planoEmEdicao ? 'Plano alterado.' : 'Plano criado.');
            await carregarPlanos();
        } catch (err) {
            mostrarErroFormulario('erro-plano', err.message);
        }
    });
}

function abrirPreco(plano) {
    planoDoPreco = plano;
    document.getElementById('preco-nome-plano').textContent = plano.nome;
    document.getElementById('preco-atual').textContent = plano.precoMensal != null
        ? `${reaisPlano(plano.precoMensal)} (fundador ${reaisPlano(plano.precoFundador)})`
        : 'sem preço';
    document.getElementById('preco-mensal').value = plano.precoMensal ?? '';
    document.getElementById('preco-fundador').value = plano.precoFundador ?? '';
    const hoje = dataLocalIso();
    const campoData = document.getElementById('preco-vigente');
    campoData.min = hoje;
    campoData.value = hoje;
    document.getElementById('preco-motivo').value = '';
    ocultarErroFormulario('erro-preco');
    modalPreco.show();
}

async function salvarPreco(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-preco');
    const corpo = {
        precoMensal: numeroOuNulo('preco-mensal') ?? 0,
        precoFundador: numeroOuNulo('preco-fundador') ?? 0,
        vigenteDesde: document.getElementById('preco-vigente').value,
        motivo: document.getElementById('preco-motivo').value.trim() || null
    };
    if (!corpo.vigenteDesde) return mostrarErroFormulario('erro-preco', 'Informe a data a partir da qual o preço vale.');

    if (!await confirmarAcao(
        `${planoDoPreco.nome}: ${reaisPlano(corpo.precoMensal)} (fundador ${reaisPlano(corpo.precoFundador)}) a partir de ${dataPlano(corpo.vigenteDesde)}. ` +
        'Vale só para novas assinaturas; quem já assina continua com o preço contratado. Confirmar?',
        { titulo: 'Novo preço', textoBotao: 'Salvar preço' })) return;

    await executarComBotaoDesabilitado(botaoSalvar(e), async function () {
        try {
            await apiFetch(`/api/plataforma/planos/${planoDoPreco.id}/precos`, { method: 'POST', body: JSON.stringify(corpo) });
            modalPreco.hide();
            mostrarToast('Preço salvo.');
            await carregarPlanos();
        } catch (err) {
            mostrarErroFormulario('erro-preco', err.message);
        }
    });
}

async function abrirHistorico(plano) {
    try {
        const precos = await apiFetch(`/api/plataforma/planos/${plano.id}/precos`);
        document.getElementById('titulo-historico-precos').textContent = `Histórico de preços · ${plano.nome}`;
        document.getElementById('corpo-historico-precos').innerHTML = precos.map(p => `
            <tr>
                <td>${dataPlano(p.vigenteDesde)}</td>
                <td class="text-end">${reaisPlano(p.precoMensal)}</td>
                <td class="text-end">${reaisPlano(p.precoFundador)}</td>
                <td class="small">${escaparHtml(p.motivo ?? '')}</td>
                <td class="small text-muted">${new Date(p.cadastradoEm).toLocaleString('pt-BR')}${p.cadastradoPor ? '<br>' + escaparHtml(p.cadastradoPor) : ''}</td>
                <td><span class="badge text-bg-${SITUACOES_PRECO[p.situacao] ?? 'secondary'}">${escaparHtml(p.situacao)}</span></td>
            </tr>`).join('');
        modalHistorico.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function salvarVagas(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-vagas');
    const corpo = {
        publico: grupoDasVagas,
        quantidade: numeroOuNulo('vagas-quantidade') ?? -1,
        motivo: document.getElementById('vagas-motivo').value.trim() || null,
        campanhaAte: document.getElementById('vagas-campanha-ate').value || null
    };
    await executarComBotaoDesabilitado(botaoSalvar(e), async function () {
        try {
            await apiFetch('/api/plataforma/vagas-fundador', { method: 'PUT', body: JSON.stringify(corpo) });
            modalVagas.hide();
            mostrarToast('Vagas de fundador alteradas.');
            await carregarVagas();
        } catch (err) {
            mostrarErroFormulario('erro-vagas', err.message);
        }
    });
}

// ---------- Excluir plano (2 passos: código no seu e-mail e confirmação) ----------

async function pedirCodigoExclusaoPlano(plano) {
    if (!await confirmarAcao(
        `Excluir o plano ${plano.nome}? Ele e o histórico de preços dele serão apagados. ` +
        'Vamos enviar um código para o seu e-mail para confirmar.',
        { titulo: 'Excluir plano', textoBotao: 'Enviar código', variante: 'perigo' })) return;

    try {
        const r = await apiFetch(`/api/plataforma/planos/${plano.id}/exclusao/codigo`, { method: 'POST' });
        planoParaExcluir = plano.id;
        document.getElementById('nome-plano-excluir').textContent = r.plano;
        document.getElementById('precos-plano-excluir').textContent = r.precos;
        document.getElementById('email-codigo-exclusao-plano').textContent = r.emailMascarado;
        document.getElementById('codigo-exclusao-plano').value = '';
        ocultarErroFormulario('erro-excluir-plano');
        iniciarRelogioExclusaoPlano(r.minutosValidade);
        modalExcluirPlano.show();
        setTimeout(() => document.getElementById('codigo-exclusao-plano').focus(), 300);
    } catch (err) {
        mostrarErro(err.message);
    }
}

function iniciarRelogioExclusaoPlano(minutos) {
    pararRelogioExclusaoPlano();
    const fim = Date.now() + minutos * 60 * 1000;
    const texto = document.getElementById('tempo-codigo-exclusao-plano');
    const botao = document.getElementById('btn-confirmar-exclusao-plano');
    botao.disabled = false;
    const atualizar = () => {
        const restante = Math.max(0, Math.round((fim - Date.now()) / 1000));
        if (restante === 0) {
            texto.textContent = 'O código expirou. Feche e clique em excluir de novo para receber outro.';
            botao.disabled = true;
            pararRelogioExclusaoPlano();
            return;
        }
        texto.textContent = `O código vale por mais ${Math.floor(restante / 60)}:${String(restante % 60).padStart(2, '0')}.`;
    };
    atualizar();
    relogioExclusaoPlano = setInterval(atualizar, 1000);
}

function pararRelogioExclusaoPlano() {
    if (relogioExclusaoPlano) clearInterval(relogioExclusaoPlano);
    relogioExclusaoPlano = null;
}

async function confirmarExclusaoPlano(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-excluir-plano');
    const codigo = document.getElementById('codigo-exclusao-plano').value.trim();
    if (!/^[0-9]{6}$/.test(codigo)) {
        mostrarErroFormulario('erro-excluir-plano', 'Digite os 6 números do código.');
        return;
    }

    await executarComBotaoDesabilitado(document.getElementById('btn-confirmar-exclusao-plano'), async () => {
        try {
            await apiFetch(`/api/plataforma/planos/${planoParaExcluir}/exclusao/confirmar`, {
                method: 'POST', body: JSON.stringify({ codigo })
            });
            modalExcluirPlano.hide();
            mostrarToast('Plano excluído.');
            await carregarPlanos();
        } catch (err) {
            mostrarErroFormulario('erro-excluir-plano', err.message);
        }
    });
}

// ---------- Reajuste de quem já assina (B2) ----------

function isoDaqui(dias) {
    const d = new Date();
    d.setDate(d.getDate() + dias);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

async function abrirReajuste(plano) {
    planoDoReajuste = plano;
    document.getElementById('reajuste-nome-plano').textContent = plano.nome;
    document.getElementById('reajuste-data').value = isoDaqui(31);
    document.getElementById('reajuste-motivo').value = '';
    document.getElementById('bloco-previa-reajuste').classList.add('d-none');
    document.getElementById('btn-agendar-reajuste').disabled = true;
    ocultarErroFormulario('erro-reajuste');
    await carregarReajustes();
    modalReajuste.show();
}

async function carregarPreviaReajuste() {
    ocultarErroFormulario('erro-reajuste');
    const data = document.getElementById('reajuste-data').value;
    if (!data) {
        mostrarErroFormulario('erro-reajuste', 'Escolha a data do novo valor.');
        return;
    }
    try {
        const previa = await apiFetch(`/api/plataforma/planos/${planoDoReajuste.id}/reajuste/previa?aplicarEm=${data}`);
        document.getElementById('resumo-previa-reajuste').innerHTML =
            `Preço de tabela em ${dataPlano(previa.aplicarEm)}: <strong>${reaisPlano(previa.precoNovo)}</strong>. ` +
            `<strong>${previa.entram}</strong> de ${previa.contas.length} assinatura(s) entram e recebem o aviso por e-mail agora.`;
        document.getElementById('corpo-previa-reajuste').innerHTML = previa.contas.length === 0
            ? '<tr><td colspan="3" class="text-muted small">Nenhuma assinatura neste plano.</td></tr>'
            : previa.contas.map(c => `<tr>
                <td>${escaparHtml(c.conta)}${c.fundador ? ' <span class="badge text-bg-light border">fundador</span>' : ''}</td>
                <td class="text-end">${reaisPlano(c.precoAtual)}</td>
                <td>${c.entra
                    ? `<span class="badge text-bg-success">Entra</span> <span class="small text-muted">→ ${reaisPlano(previa.precoNovo)}</span>`
                    : `<span class="badge text-bg-secondary">Fica de fora</span> <span class="small text-muted">${escaparHtml(c.motivo ?? '')}</span>`}</td>
            </tr>`).join('');
        document.getElementById('bloco-previa-reajuste').classList.remove('d-none');
        document.getElementById('btn-agendar-reajuste').disabled = previa.entram === 0;
    } catch (err) {
        document.getElementById('btn-agendar-reajuste').disabled = true;
        mostrarErroFormulario('erro-reajuste', err.message);
    }
}

async function agendarReajuste() {
    const data = document.getElementById('reajuste-data').value;
    const confirmado = await confirmarAcao(
        `Agendar o reajuste do plano ${planoDoReajuste.nome} para ${dataPlano(data)}? O aviso sai por e-mail agora para os administradores de cada conta que entra.`,
        { titulo: 'Reajustar assinantes', textoBotao: 'Agendar e avisar' });
    if (!confirmado) return;

    try {
        const r = await apiFetch(`/api/plataforma/planos/${planoDoReajuste.id}/reajuste`, {
            method: 'POST',
            body: JSON.stringify({ aplicarEm: data, motivo: document.getElementById('reajuste-motivo').value || null })
        });
        mostrarToast(`${r.agendados} reajuste(s) agendado(s).${r.semDestinatario ? ` ${r.semDestinatario} sem administrador com e-mail confirmado (aviso não saiu).` : ''}`);
        document.getElementById('bloco-previa-reajuste').classList.add('d-none');
        document.getElementById('btn-agendar-reajuste').disabled = true;
        await carregarReajustes();
    } catch (err) {
        mostrarErroFormulario('erro-reajuste', err.message);
    }
}

async function carregarReajustes() {
    const corpo = document.getElementById('corpo-reajustes');
    try {
        const lista = await apiFetch(`/api/plataforma/planos/${planoDoReajuste.id}/reajustes`);
        const lotesComAgendado = new Set(lista.filter(r => r.status === 1).map(r => r.loteId));
        const lotesComBotao = new Set();
        corpo.innerHTML = lista.length === 0
            ? '<tr><td colspan="6" class="text-muted small">Nenhum reajuste ainda.</td></tr>'
            : lista.map(r => {
                const s = SITUACOES_REAJUSTE[r.status] ?? { texto: '—', cor: 'secondary' };
                // Um botão por lote, na primeira linha agendada dele.
                const botao = lotesComAgendado.has(r.loteId) && r.status === 1 && !lotesComBotao.has(r.loteId)
                    ? (lotesComBotao.add(r.loteId), `<button type="button" class="btn btn-outline-danger btn-sm btn-cancelar-lote" data-lote="${r.loteId}" title="Cancela todos os agendados deste lote">Cancelar lote</button>`)
                    : '';
                return `<tr>
                    <td>${escaparHtml(r.conta)}${r.motivo ? `<div class="small text-muted">${escaparHtml(r.motivo)}</div>` : ''}</td>
                    <td class="text-end text-nowrap">${reaisPlano(r.precoAnterior)} → <strong>${reaisPlano(r.precoNovo)}</strong></td>
                    <td>${dataPlano(r.aplicarEm)}</td>
                    <td><span class="badge text-bg-${s.cor}">${s.texto}</span>${r.detalhe ? `<div class="small text-muted">${escaparHtml(r.detalhe)}</div>` : ''}</td>
                    <td class="small">${r.avisadoEm ? dataPlano(r.avisadoEm) : '<span class="text-warning">sem aviso</span>'}</td>
                    <td>${botao}</td>
                </tr>`;
            }).join('');
    } catch (err) {
        corpo.innerHTML = `<tr><td colspan="6" class="text-danger small">${escaparHtml(err.message)}</td></tr>`;
    }
}

async function cancelarLoteReajuste(loteId) {
    const confirmado = await confirmarAcao(
        'Cancelar todos os reajustes ainda agendados deste lote? Os clientes continuam pagando o valor atual (não sai e-mail).',
        { titulo: 'Cancelar reajuste', textoBotao: 'Cancelar lote', variante: 'perigo' });
    if (!confirmado) return;
    try {
        const r = await apiFetch(`/api/plataforma/reajustes/lotes/${loteId}/cancelar`, { method: 'POST' });
        mostrarToast(`${r.cancelados} reajuste(s) cancelado(s).`);
        await carregarReajustes();
    } catch (err) {
        mostrarErroFormulario('erro-reajuste', err.message);
    }
}
