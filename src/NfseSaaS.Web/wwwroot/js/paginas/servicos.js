const podeEditarServico = document.getElementById('btn-novo-servico') !== null;

let tabelaServicos;
let modalServico;
let empresaAtualIdServicos;

document.addEventListener('DOMContentLoaded', function () {
    const colunas = [
        { data: 'descricao' },
        { data: 'codigoTributacaoNacional' },
        { data: 'codigoNbs' },
        { data: 'valorPadrao', render: v => Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) },
        {
            data: 'ativo',
            render: v => v
                ? '<span class="badge text-bg-success">Ativo</span>'
                : '<span class="badge text-bg-secondary">Inativo</span>'
        }
    ];

    if (podeEditarServico) {
        colunas.push({
            data: null,
            orderable: false,
            render: (data, type, servico) => `
                <button type="button" class="btn btn-sm btn-outline-primary btn-editar" data-id="${servico.id}">
                    <i class="bi bi-pencil"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-warning btn-alternar-ativo" data-id="${servico.id}" data-ativo="${servico.ativo}">
                    <i class="bi ${servico.ativo ? 'bi-toggle-on' : 'bi-toggle-off'}"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-danger btn-excluir" data-id="${servico.id}">
                    <i class="bi bi-trash"></i>
                </button>
            `
        });
    }

    tabelaServicos = new DataTable('#tabela-servicos', {
        columns: colunas,
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    configurarExportacao(tabelaServicos, 'Servicos');

    if (podeEditarServico) {
        modalServico = new bootstrap.Modal(document.getElementById('modal-servico'));
        document.getElementById('btn-novo-servico').addEventListener('click', abrirModalNovoServico);
        document.getElementById('form-servico').addEventListener('submit', salvarServico);

        document.getElementById('tabela-servicos').addEventListener('click', async function (e) {
            const botao = e.target.closest('button');
            if (!botao) return;
            const id = botao.dataset.id;

            if (botao.classList.contains('btn-editar')) await abrirModalEditarServico(id);
            else if (botao.classList.contains('btn-alternar-ativo')) await alternarAtivoServico(id, botao.dataset.ativo === 'true');
            else if (botao.classList.contains('btn-excluir')) await excluirServico(id);
        });
    }

    empresaAtualIdServicos = obterEmpresaAtualId();
    carregarServicos();
});

async function carregarServicos() {
    if (!empresaAtualIdServicos) return;

    try {
        const resultado = await apiFetch(`/api/servicos?empresaId=${empresaAtualIdServicos}&pageSize=200&incluirInativos=true`);
        tabelaServicos.clear();
        tabelaServicos.rows.add(resultado.items);
        tabelaServicos.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function limparFormularioServico() {
    document.getElementById('form-servico').reset();
    document.getElementById('servicoId').value = '';
    document.getElementById('erro-servico').classList.add('d-none');
}

function abrirModalNovoServico() {
    limparFormularioServico();
    document.getElementById('titulo-modal-servico').textContent = 'Novo serviço';
    modalServico.show();
}

async function abrirModalEditarServico(id) {
    limparFormularioServico();
    document.getElementById('titulo-modal-servico').textContent = 'Editar serviço';

    try {
        const servico = await apiFetch(`/api/servicos/${id}`);

        document.getElementById('servicoId').value = servico.id;
        document.getElementById('descricao').value = servico.descricao;
        document.getElementById('codigoTributacaoNacional').value = servico.codigoTributacaoNacional;
        document.getElementById('codigoNbs').value = servico.codigoNbs;
        document.getElementById('valorPadrao').value = servico.valorPadrao;

        modalServico.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function salvarServico(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-servico');

    const id = document.getElementById('servicoId').value;
    const payload = {
        empresaId: empresaAtualIdServicos,
        descricao: document.getElementById('descricao').value,
        codigoTributacaoNacional: document.getElementById('codigoTributacaoNacional').value,
        codigoNbs: document.getElementById('codigoNbs').value,
        valorPadrao: parseFloat(document.getElementById('valorPadrao').value)
    };

    try {
        if (id) {
            await apiFetch(`/api/servicos/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            await apiFetch('/api/servicos', { method: 'POST', body: JSON.stringify(payload) });
        }

        modalServico.hide();
        await carregarServicos();
        mostrarToast('Serviço salvo com sucesso.');
    } catch (err) {
        mostrarErroFormulario('erro-servico', err.message);
    }
}

async function alternarAtivoServico(id, ativoAtualmente) {
    const acao = ativoAtualmente ? 'desativar' : 'reativar';
    try {
        await apiFetch(`/api/servicos/${id}/${acao}`, { method: 'POST' });
        await carregarServicos();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function excluirServico(id) {
    const confirmado = await confirmarAcao('Excluir este serviço?', { titulo: 'Excluir serviço', textoBotao: 'Excluir', variante: 'perigo' });
    if (!confirmado) return;

    try {
        await apiFetch(`/api/servicos/${id}`, { method: 'DELETE' });
        await carregarServicos();
        mostrarToast('Serviço excluído.');
    } catch (err) {
        mostrarErro(err.message);
    }
}
