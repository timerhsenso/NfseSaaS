let tabelaGrupos;
let modalGrupo;
let modalMatriz;
let grupoMatrizAtualId;
let telasCache;

document.addEventListener('DOMContentLoaded', function () {
    const podeAlterar = window.__gruposPermissoes.podeAlterar;
    const podeExcluir = window.__gruposPermissoes.podeExcluir;

    tabelaGrupos = new DataTable('#tabela-grupos', {
        columns: [
            { data: 'nome' },
            { data: 'descricao', render: v => v || '—' },
            { data: 'quantidadeUsuarios' },
            {
                data: 'padrao',
                render: v => v ? '<span class="badge text-bg-secondary">Padrão</span>' : '<span class="badge text-bg-light border">Personalizado</span>'
            },
            {
                data: null,
                orderable: false,
                render: (data, type, grupo) => {
                    let botoes = `
                        <button type="button" class="btn btn-sm btn-outline-primary btn-matriz" data-id="${grupo.id}" data-nome="${grupo.nome}" data-admin="${grupo.ehAdministrador}" title="Permissões">
                            <i class="bi bi-shield-check"></i>
                        </button>
                    `;
                    if (podeAlterar) {
                        botoes += `
                            <button type="button" class="btn btn-sm btn-outline-secondary btn-editar-grupo" data-id="${grupo.id}" data-nome="${grupo.nome}" data-descricao="${grupo.descricao || ''}" title="Renomear">
                                <i class="bi bi-pencil"></i>
                            </button>
                        `;
                    }
                    if (podeExcluir && !grupo.ehAdministrador) {
                        botoes += `
                            <button type="button" class="btn btn-sm btn-outline-danger btn-excluir-grupo" data-id="${grupo.id}" title="Excluir">
                                <i class="bi bi-trash"></i>
                            </button>
                        `;
                    }
                    return botoes;
                }
            }
        ],
        order: [[0, 'asc']],
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    modalGrupo = new bootstrap.Modal(document.getElementById('modal-grupo'));
    modalMatriz = new bootstrap.Modal(document.getElementById('modal-matriz'));

    const btnNovo = document.getElementById('btn-novo-grupo');
    if (btnNovo) btnNovo.addEventListener('click', abrirModalNovoGrupo);

    document.getElementById('form-grupo').addEventListener('submit', salvarGrupo);
    document.getElementById('btn-salvar-matriz').addEventListener('click', salvarMatriz);

    document.getElementById('tabela-grupos').addEventListener('click', async function (e) {
        const botao = e.target.closest('button');
        if (!botao) return;

        if (botao.classList.contains('btn-matriz')) await abrirModalMatriz(botao.dataset.id, botao.dataset.nome, botao.dataset.admin === 'true');
        else if (botao.classList.contains('btn-editar-grupo')) abrirModalEditarGrupo(botao.dataset.id, botao.dataset.nome, botao.dataset.descricao);
        else if (botao.classList.contains('btn-excluir-grupo')) await excluirGrupo(botao.dataset.id);
    });

    carregarGrupos();
});

async function carregarGrupos() {
    try {
        const grupos = await apiFetch('/api/grupos');
        tabelaGrupos.clear();
        tabelaGrupos.rows.add(grupos);
        tabelaGrupos.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function limparFormularioGrupo() {
    document.getElementById('form-grupo').reset();
    document.getElementById('grupoId').value = '';
    document.getElementById('erro-grupo').classList.add('d-none');
}

function abrirModalNovoGrupo() {
    limparFormularioGrupo();
    document.getElementById('titulo-modal-grupo').textContent = 'Novo grupo';
    modalGrupo.show();
}

function abrirModalEditarGrupo(id, nome, descricao) {
    limparFormularioGrupo();
    document.getElementById('titulo-modal-grupo').textContent = 'Renomear grupo';
    document.getElementById('grupoId').value = id;
    document.getElementById('grupoNome').value = nome;
    document.getElementById('grupoDescricao').value = descricao;
    modalGrupo.show();
}

async function salvarGrupo(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-grupo');

    const id = document.getElementById('grupoId').value;
    const payload = {
        nome: document.getElementById('grupoNome').value,
        descricao: document.getElementById('grupoDescricao').value || null
    };

    try {
        if (id) {
            await apiFetch(`/api/grupos/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            await apiFetch('/api/grupos', { method: 'POST', body: JSON.stringify(payload) });
        }

        modalGrupo.hide();
        await carregarGrupos();
        mostrarToast('Grupo salvo.');
    } catch (err) {
        mostrarErroFormulario('erro-grupo', err.message);
    }
}

async function excluirGrupo(id) {
    const confirmado = await confirmarAcao(
        'Excluir este grupo? Só funciona se não houver usuário vinculado.',
        { titulo: 'Excluir grupo', textoBotao: 'Excluir', variante: 'perigo' }
    );
    if (!confirmado) return;

    try {
        await apiFetch(`/api/grupos/${id}`, { method: 'DELETE' });
        await carregarGrupos();
        mostrarToast('Grupo excluído.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function abrirModalMatriz(id, nome, ehAdministrador) {
    grupoMatrizAtualId = id;
    document.getElementById('matriz-grupo-nome').textContent = nome;

    const avisoAdmin = document.getElementById('matriz-aviso-admin');
    const btnSalvar = document.getElementById('btn-salvar-matriz');
    avisoAdmin.classList.toggle('d-none', !ehAdministrador);
    btnSalvar.classList.toggle('d-none', ehAdministrador || !window.__gruposPermissoes.podeAlterar);

    try {
        const detalhe = await apiFetch(`/api/grupos/${id}`);
        renderizarMatriz(detalhe.permissoes, ehAdministrador);
        modalMatriz.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function renderizarMatriz(permissoes, somenteLeitura) {
    const corpo = document.getElementById('matriz-corpo');
    corpo.innerHTML = permissoes.map(p => `
        <tr data-tela-id="${p.telaId}">
            <td>${p.telaNome}</td>
            <td class="text-center"><input type="checkbox" class="form-check-input chk-incluir" ${p.incluir ? 'checked' : ''} ${somenteLeitura ? 'disabled' : ''} /></td>
            <td class="text-center"><input type="checkbox" class="form-check-input chk-alterar" ${p.alterar ? 'checked' : ''} ${somenteLeitura ? 'disabled' : ''} /></td>
            <td class="text-center"><input type="checkbox" class="form-check-input chk-excluir" ${p.excluir ? 'checked' : ''} ${somenteLeitura ? 'disabled' : ''} /></td>
            <td class="text-center"><input type="checkbox" class="form-check-input chk-consultar" ${p.consultar ? 'checked' : ''} ${somenteLeitura ? 'disabled' : ''} /></td>
        </tr>
    `).join('');
}

async function salvarMatriz() {
    const linhas = document.querySelectorAll('#matriz-corpo tr');
    const permissoes = Array.from(linhas).map(linha => ({
        telaId: linha.dataset.telaId,
        incluir: linha.querySelector('.chk-incluir').checked,
        alterar: linha.querySelector('.chk-alterar').checked,
        excluir: linha.querySelector('.chk-excluir').checked,
        consultar: linha.querySelector('.chk-consultar').checked
    }));

    try {
        await apiFetch(`/api/grupos/${grupoMatrizAtualId}/permissoes`, {
            method: 'PUT',
            body: JSON.stringify({ permissoes })
        });

        modalMatriz.hide();
        mostrarToast('Permissões atualizadas — usuários desse grupo precisam relogar pra pegar a mudança.');
    } catch (err) {
        mostrarErro(err.message);
    }
}
