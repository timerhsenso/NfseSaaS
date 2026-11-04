// Tela de índices econômicos (tabela GLOBAL). Botões de escrita só existem
// no HTML para o operador da plataforma — a API confere de novo.
const operadorIndices = document.getElementById('btn-lancar-indice') !== null;

// FonteIndiceEconomico (Domain.Enums), serializado como número.
const ROTULOS_FONTE_INDICE = {
    1: '<span class="badge text-bg-light border">Banco Central</span>',
    2: '<span class="badge text-bg-warning">Manual</span>'
};

let tabelaIndices;
let modalIndiceManual;
let indicesDisponiveis = [];

function indiceSelecionado() {
    return parseInt(document.getElementById('indice-selecionado').value, 10);
}

function nomeIndice(indice) {
    return indicesDisponiveis.find(i => i.indice === indice)?.nome ?? '';
}

// 'AAAA-MM-DD' → 'MM/AAAA' sem passar por Date (evita fuso).
function formatarCompetencia(iso) {
    const [ano, mes] = String(iso).split('-');
    return `${mes}/${ano}`;
}

function formatarPercentual(valor, casas) {
    return Number(valor).toLocaleString('pt-BR', { minimumFractionDigits: casas ?? 2, maximumFractionDigits: 4 }) + '%';
}

document.addEventListener('DOMContentLoaded', async function () {
    document.getElementById('acumulado-data').value = dataLocalIso();

    const colunas = [
        { data: 'competencia', render: (v, type) => type === 'display' ? formatarCompetencia(v) : v },
        {
            data: 'variacao',
            className: 'text-end',
            render: (v, type) => type === 'display'
                ? `<span class="${v < 0 ? 'text-danger' : ''}">${formatarPercentual(v)}</span>`
                : v
        },
        { data: 'fonte', render: v => ROTULOS_FONTE_INDICE[v] ?? v },
        {
            data: 'atualizadoEm',
            className: 'text-nowrap',
            render: (v, type) => type === 'display'
                ? new Date(v).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
                : v
        }
    ];

    if (operadorIndices) {
        colunas.push({
            data: null,
            orderable: false,
            className: 'text-nowrap',
            render: (data, type, linha) => `
                <button type="button" class="btn btn-sm btn-outline-primary btn-editar-indice"
                        data-competencia="${linha.competencia}" data-variacao="${linha.variacao}" title="Corrigir (vira Manual)">
                    <i class="bi bi-pencil"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-danger btn-excluir-indice"
                        data-id="${linha.id}" data-competencia="${linha.competencia}" data-fonte="${linha.fonte}" title="Excluir">
                    <i class="bi bi-trash"></i>
                </button>`
        });
    }

    tabelaIndices = new DataTable('#tabela-indices', {
        columns: colunas,
        order: [[0, 'desc']],
        pageLength: 12,
        lengthMenu: [12, 24, 48],
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    document.getElementById('indice-selecionado').addEventListener('change', recarregarTudo);
    document.getElementById('acumulado-data').addEventListener('change', carregarAcumulado);

    if (operadorIndices) {
        modalIndiceManual = new bootstrap.Modal(document.getElementById('modal-indice-manual'));
        document.getElementById('btn-lancar-indice').addEventListener('click', () => abrirModalManual());
        document.getElementById('btn-importar-indices').addEventListener('click', importarAgora);
        document.getElementById('form-indice-manual').addEventListener('submit', salvarManual);

        document.getElementById('tabela-indices').addEventListener('click', function (e) {
            const editar = e.target.closest('.btn-editar-indice');
            if (editar) {
                abrirModalManual(editar.dataset.competencia, editar.dataset.variacao);
                return;
            }
            const excluir = e.target.closest('.btn-excluir-indice');
            if (excluir) executarComBotaoDesabilitado(excluir, () => excluirIndice(excluir.dataset));
        });
    }

    try {
        await carregarIndicesDisponiveis();
        await recarregarTudo();
    } catch (erro) {
        mostrarErro(erro.message);
    }
});

async function carregarIndicesDisponiveis() {
    indicesDisponiveis = await apiFetch('/api/indices-economicos/disponiveis');
    const select = document.getElementById('indice-selecionado');
    const atual = select.value;
    select.innerHTML = indicesDisponiveis
        .map(i => `<option value="${i.indice}">${escaparHtml(i.nome)}</option>`)
        .join('');
    if (atual !== '') select.value = atual;
    atualizarInfoIndice();
}

function atualizarInfoIndice() {
    const info = indicesDisponiveis.find(i => i.indice === indiceSelecionado());
    if (!info) return;
    const partes = [];
    if (info.serie) partes.push(info.serie);
    partes.push(info.ultimaCompetencia ? `último mês: ${formatarCompetencia(info.ultimaCompetencia)}` : 'sem dados ainda');
    document.getElementById('indice-info').textContent = partes.join(' · ');
}

async function recarregarTudo() {
    atualizarInfoIndice();
    await Promise.all([carregarTabela(), carregarAcumulado()]);
}

async function carregarTabela() {
    try {
        const linhas = await apiFetch(`/api/indices-economicos?indice=${indiceSelecionado()}`);
        tabelaIndices.clear().rows.add(linhas ?? []).draw();
    } catch (erro) {
        mostrarErro(erro.message);
    }
}

async function carregarAcumulado() {
    const data = document.getElementById('acumulado-data').value || dataLocalIso();
    const indisponivel = document.getElementById('acumulado-indisponivel');
    const resultado = document.getElementById('acumulado-resultado');
    const tabela = document.getElementById('tabela-acumulado-meses');

    try {
        const r = await apiFetch(`/api/indices-economicos/acumulado?indice=${indiceSelecionado()}&dataReferencia=${data}`);

        indisponivel.classList.toggle('d-none', r.disponivel);
        resultado.classList.toggle('d-none', !r.disponivel);
        indisponivel.textContent = r.disponivel ? '' : r.mensagem;

        if (r.disponivel) {
            const percentual = document.getElementById('acumulado-percentual');
            percentual.textContent = formatarPercentual(r.percentualAcumulado, 2);
            percentual.classList.toggle('text-danger', r.percentualAcumulado < 0);
            document.getElementById('acumulado-janela').textContent =
                `${nomeIndice(r.indice)} de ${formatarCompetencia(r.competenciaInicio)} a ${formatarCompetencia(r.competenciaFim)} ` +
                `(12 meses publicados até ${new Date(r.dataReferencia + 'T12:00:00').toLocaleDateString('pt-BR')})` +
                (r.percentualAcumulado < 0 ? ' — acumulado negativo' : '');
        }

        const meses = r.meses ?? [];
        tabela.classList.toggle('d-none', meses.length === 0);
        tabela.querySelector('tbody').innerHTML = meses.map(m => `
            <tr>
                <td>${formatarCompetencia(m.competencia)}</td>
                <td class="text-end ${m.variacao < 0 ? 'text-danger' : ''}">${formatarPercentual(m.variacao)}</td>
                <td>${ROTULOS_FONTE_INDICE[m.fonte] ?? m.fonte}</td>
            </tr>`).join('');
    } catch (erro) {
        mostrarErro(erro.message);
    }
}

function abrirModalManual(competencia, variacao) {
    ocultarErroFormulario('erro-indice-manual');
    document.getElementById('manual-nome-indice').textContent = nomeIndice(indiceSelecionado());
    document.getElementById('manual-competencia').value = competencia ? competencia.substring(0, 7) : '';
    document.getElementById('manual-variacao').value = variacao ?? '';
    modalIndiceManual.show();
}

async function salvarManual(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-indice-manual');

    const competencia = document.getElementById('manual-competencia').value; // AAAA-MM
    const variacao = parseFloat(document.getElementById('manual-variacao').value);

    try {
        await apiFetch('/api/indices-economicos', {
            method: 'PUT',
            body: JSON.stringify({ indice: indiceSelecionado(), competencia: `${competencia}-01`, variacao: variacao })
        });
        modalIndiceManual.hide();
        mostrarToast('Variação salva como Manual.');
        await carregarIndicesDisponiveis();
        await recarregarTudo();
    } catch (erro) {
        mostrarErroFormulario('erro-indice-manual', erro.message);
    }
}

async function excluirIndice(dados) {
    const manual = dados.fonte === '2';
    const mensagem = manual
        ? `Excluir o valor manual de ${formatarCompetencia(dados.competencia)}? A próxima importação traz de volta o valor do Banco Central.`
        : `Excluir ${formatarCompetencia(dados.competencia)}? Se ainda estiver nos últimos 24 meses, a próxima importação traz o mês de volta.`;

    if (!await confirmarAcao(mensagem, { titulo: 'Excluir variação', textoBotao: 'Excluir', variante: 'perigo' })) return;

    try {
        await apiFetch(`/api/indices-economicos/${dados.id}`, { method: 'DELETE' });
        mostrarToast('Variação excluída.');
        await carregarIndicesDisponiveis();
        await recarregarTudo();
    } catch (erro) {
        mostrarErro(erro.message);
    }
}

async function importarAgora() {
    const botao = document.getElementById('btn-importar-indices');
    const caixa = document.getElementById('resultado-importacao');

    await executarComBotaoDesabilitado(botao, async function () {
        try {
            const r = await apiFetch('/api/indices-economicos/importar', { method: 'POST' });
            const falhas = r.indices.filter(i => i.erro);
            caixa.className = 'alert ' + (falhas.length === 0 ? 'alert-success' : 'alert-warning');
            caixa.innerHTML = r.indices.map(i => {
                const nome = escaparHtml(nomeIndice(i.indice) || i.indice);
                return i.erro
                    ? `<div><strong>${nome}</strong>: falhou — ${escaparHtml(i.erro)}</div>`
                    : `<div><strong>${nome}</strong>: ${i.inseridos} novo(s), ${i.atualizados} atualizado(s)` +
                      (i.manuaisPreservados ? `, ${i.manuaisPreservados} manual(is) preservado(s)` : '') +
                      (i.ultimaCompetencia ? ` — último mês ${formatarCompetencia(i.ultimaCompetencia)}` : '') + '</div>';
            }).join('');
            await carregarIndicesDisponiveis();
            await recarregarTudo();
        } catch (erro) {
            mostrarErro(erro.message);
        }
    });
}
