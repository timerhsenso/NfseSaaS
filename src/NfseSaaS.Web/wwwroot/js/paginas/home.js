// NfseStatus (Domain.Enums) serializado como número pela API — mesmo
// mapeamento usado em wwwroot/js/paginas/nfse.js, mantido em sincronia
// manualmente (os dois arquivos leem o mesmo enum do backend, mas não
// há um jeito de compartilhar essa tabela entre C# e JS sem gerar
// código — replicar é o custo aceito).
const ROTULOS_STATUS_DASHBOARD = {
    0: { texto: 'Rascunho', cor: 'secondary' },
    1: { texto: 'Processando', cor: 'info' },
    2: { texto: 'Autorizada', cor: 'success' },
    3: { texto: 'Rejeitada', cor: 'danger' },
    4: { texto: 'Cancelada', cor: 'dark' },
    5: { texto: 'Substituída', cor: 'secondary' }
};

async function carregarDashboard() {
    // Empresas: usado tanto pro card de contagem quanto pra resolver o
    // nome da empresa na lista de notas recentes (e como base pra
    // agregar Clientes/Serviços, que são escopados por Empresa, não
    // por Tenant inteiro).
    let empresas = [];
    try {
        const respostaEmpresas = await apiFetch('/api/empresas?pageSize=100');
        empresas = respostaEmpresas?.items ?? [];
        document.getElementById('contagem-empresas').textContent = respostaEmpresas?.totalCount ?? empresas.length;
    } catch (err) {
        document.getElementById('contagem-empresas').textContent = '—';
    }

    const empresasPorId = {};
    empresas.forEach(e => { empresasPorId[e.id] = e.razaoSocial; });

    carregarAgregadoPorEmpresa(empresas, '/api/clientes', 'contagem-clientes');
    carregarAgregadoPorEmpresa(empresas, '/api/servicos', 'contagem-servicos');
    carregarContratosComReajustePendente(empresas);

    try {
        const respostaNfse = await apiFetch('/api/nfse?pageSize=100');
        const notas = respostaNfse?.items ?? [];

        document.getElementById('contagem-nfse').textContent = respostaNfse?.totalCount ?? notas.length;
        renderizarNotasRecentes(notas.slice(0, 8), empresasPorId);
        renderizarResumoStatus(notas);
    } catch (err) {
        document.getElementById('contagem-nfse').textContent = '—';
        document.querySelector('#tabela-nfse-recentes tbody').innerHTML =
            '<tr><td colspan="5" class="text-center text-danger py-4">Não foi possível carregar as notas.</td></tr>';
        document.getElementById('resumo-status-nfse').innerHTML = '<span class="text-danger">Não foi possível carregar.</span>';
    }
}

// Clientes/Serviços não têm listagem por Tenant inteiro (a API exige
// empresaId — um Tenant pode ter várias Empresas). Agrega aqui somando
// o totalCount de cada Empresa, uma chamada por Empresa (pageSize=1 pra
// não trazer dado desnecessário).
async function carregarAgregadoPorEmpresa(empresas, endpoint, elementId) {
    const elemento = document.getElementById(elementId);
    if (empresas.length === 0) {
        elemento.textContent = '0';
        return;
    }

    try {
        const respostas = await Promise.all(
            empresas.map(e => apiFetch(`${endpoint}?empresaId=${e.id}&pageSize=1`).catch(() => null))
        );
        const total = respostas.reduce((soma, r) => soma + (r?.totalCount ?? 0), 0);
        elemento.textContent = total;
    } catch (err) {
        elemento.textContent = '—';
    }
}

function renderizarNotasRecentes(notas, empresasPorId) {
    const corpo = document.querySelector('#tabela-nfse-recentes tbody');

    if (notas.length === 0) {
        corpo.innerHTML = '<tr><td colspan="5" class="text-center text-muted py-4">Nenhuma nota fiscal emitida ainda.</td></tr>';
        return;
    }

    corpo.innerHTML = notas.map(n => {
        const rotulo = ROTULOS_STATUS_DASHBOARD[n.status] ?? { texto: 'Desconhecido', cor: 'secondary' };
        const valor = Number(n.valorServico).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
        const emissao = n.dataEmissao ? new Date(n.dataEmissao).toLocaleDateString('pt-BR') : '—';
        const empresa = empresasPorId[n.empresaId] ?? '—';

        return `<tr>
            <td>${n.numeroDps}/${n.serieDps}</td>
            <td>${empresa}</td>
            <td>${valor}</td>
            <td><span class="badge text-bg-${rotulo.cor}">${rotulo.texto}</span></td>
            <td>${emissao}</td>
        </tr>`;
    }).join('');
}

// Contratos, assim como Clientes/Serviços, são escopados por Empresa —
// agrega buscando de cada Empresa (só ativos, pageSize=100 cobre bem o
// volume esperado) e contando quantos não estão "Em dia" (situacao 0).
// Card clicável leva pra tela de Contratos pra revisar quais são.
async function carregarContratosComReajustePendente(empresas) {
    const elemento = document.getElementById('contagem-contratos-reajuste');
    const detalhe = document.getElementById('detalhe-contratos-reajuste');

    if (empresas.length === 0) {
        elemento.textContent = '0';
        return;
    }

    try {
        const respostas = await Promise.all(
            empresas.map(e => apiFetch(`/api/contratos?empresaId=${e.id}&pageSize=100`).catch(() => null))
        );

        let vencendo = 0, vencidos = 0;
        respostas.forEach(r => {
            (r?.items ?? []).forEach(c => {
                if (c.situacao === 1) vencendo++;
                else if (c.situacao === 2) vencidos++;
            });
        });

        elemento.textContent = vencendo + vencidos;
        detalhe.textContent = vencidos > 0
            ? `${vencidos} vencido(s), ${vencendo} vencendo em breve`
            : vencendo > 0
                ? `${vencendo} vencendo em breve`
                : 'Nenhum — todos em dia';
    } catch (err) {
        elemento.textContent = '—';
    }
}

function renderizarResumoStatus(notas) {
    const container = document.getElementById('resumo-status-nfse');

    if (notas.length === 0) {
        container.innerHTML = '<span class="text-muted">Nenhuma nota fiscal emitida ainda.</span>';
        return;
    }

    const contagemPorStatus = {};
    notas.forEach(n => { contagemPorStatus[n.status] = (contagemPorStatus[n.status] ?? 0) + 1; });

    container.innerHTML = Object.entries(contagemPorStatus)
        .sort((a, b) => b[1] - a[1])
        .map(([status, quantidade]) => {
            const rotulo = ROTULOS_STATUS_DASHBOARD[status] ?? { texto: 'Desconhecido', cor: 'secondary' };
            return `<div class="d-flex justify-content-between align-items-center">
                <span class="badge text-bg-${rotulo.cor}">${rotulo.texto}</span>
                <strong>${quantidade}</strong>
            </div>`;
        }).join('');
}

carregarDashboard();
