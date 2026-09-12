let tabelaUsuarios;
let modalGrupoUsuario;
let gruposCache = [];

document.addEventListener('DOMContentLoaded', async function () {
    tabelaUsuarios = new DataTable('#tabela-usuarios', {
        columns: [
            { data: 'email' },
            { data: 'grupo' },
            {
                data: null,
                render: (data, type, usuario) => {
                    if (usuario.pendente) return '<span class="badge text-bg-warning">Pendente</span>';
                    if (usuario.bloqueado) return '<span class="badge text-bg-danger">Bloqueado</span>';
                    return '<span class="badge text-bg-success">Ativo</span>';
                }
            },
            {
                data: null,
                orderable: false,
                render: (data, type, usuario) => montarAcoes(usuario)
            }
        ],
        order: [[0, 'asc']],
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    modalGrupoUsuario = new bootstrap.Modal(document.getElementById('modal-grupo-usuario'));

    document.getElementById('tabela-usuarios').addEventListener('click', async function (e) {
        const botao = e.target.closest('button');
        if (!botao) return;
        const id = botao.dataset.id;

        if (botao.classList.contains('btn-reenviar')) await reenviarConvite(id);
        else if (botao.classList.contains('btn-editar-grupo')) abrirModalGrupoUsuario(botao.dataset.id, botao.dataset.email, botao.dataset.grupoId);
        else if (botao.classList.contains('btn-resetar-senha')) await resetarSenha(botao.dataset.id, botao.dataset.email);
        else if (botao.classList.contains('btn-bloquear')) await bloquearUsuario(id);
        else if (botao.classList.contains('btn-desbloquear')) await desbloquearUsuario(id);
        else if (botao.classList.contains('btn-excluir-usuario')) await excluirUsuario(id);
    });

    document.getElementById('form-convite').addEventListener('submit', convidar);
    document.getElementById('form-grupo-usuario').addEventListener('submit', salvarGrupoUsuario);

    await carregarGruposParaSelects();
    carregarUsuarios();
});

async function carregarGruposParaSelects() {
    try {
        gruposCache = await apiFetch('/api/grupos');
        const opcoes = gruposCache.map(g => `<option value="${g.id}">${g.nome}</option>`).join('');
        document.getElementById('grupoId').innerHTML = opcoes;
        document.getElementById('grupo-usuario-novo').innerHTML = opcoes;
    } catch (err) {
        mostrarErro(err.message);
    }
}

function montarAcoes(usuario) {
    if (usuario.pendente) {
        return `
            <button type="button" class="btn btn-sm btn-outline-primary btn-reenviar" data-id="${usuario.id}" title="Reenviar e-mail de convite">
                <i class="bi bi-envelope-arrow-up"></i>
            </button>
            <button type="button" class="btn btn-sm btn-outline-danger btn-excluir-usuario" data-id="${usuario.id}" title="Excluir convite">
                <i class="bi bi-trash"></i>
            </button>
        `;
    }

    if (usuario.ehVoce) {
        return `
            <button type="button" class="btn btn-sm btn-outline-primary btn-editar-grupo" data-id="${usuario.id}" data-email="${usuario.email}" data-grupo-id="${usuario.grupoId || ''}" title="Alterar grupo">
                <i class="bi bi-person-gear"></i>
            </button>
            <span class="text-muted ms-1">Você</span>
        `;
    }

    const botaoGrupo = `
        <button type="button" class="btn btn-sm btn-outline-primary btn-editar-grupo" data-id="${usuario.id}" data-email="${usuario.email}" data-grupo-id="${usuario.grupoId || ''}" title="Alterar grupo">
            <i class="bi bi-person-gear"></i>
        </button>
    `;

    const botaoResetarSenha = `
        <button type="button" class="btn btn-sm btn-outline-secondary btn-resetar-senha" data-id="${usuario.id}" data-email="${usuario.email}" title="Resetar senha">
            <i class="bi bi-key"></i>
        </button>
    `;

    const botaoBloqueio = usuario.bloqueado
        ? `<button type="button" class="btn btn-sm btn-outline-success btn-desbloquear" data-id="${usuario.id}" title="Desbloquear acesso"><i class="bi bi-unlock"></i></button>`
        : `<button type="button" class="btn btn-sm btn-outline-warning btn-bloquear" data-id="${usuario.id}" title="Bloquear acesso"><i class="bi bi-lock"></i></button>`;

    const botaoExcluir = `
        <button type="button" class="btn btn-sm btn-outline-danger btn-excluir-usuario" data-id="${usuario.id}" title="Excluir usuário">
            <i class="bi bi-trash"></i>
        </button>
    `;

    return botaoGrupo + botaoResetarSenha + botaoBloqueio + botaoExcluir;
}

async function carregarUsuarios() {
    try {
        const usuarios = await apiFetch('/api/auth/usuarios');
        tabelaUsuarios.clear();
        tabelaUsuarios.rows.add(usuarios);
        tabelaUsuarios.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function convidar(e) {
    e.preventDefault();
    const erroDiv = document.getElementById('erro-convite');
    const sucessoDiv = document.getElementById('sucesso-convite');
    erroDiv.classList.add('d-none');
    sucessoDiv.classList.add('d-none');

    try {
        const resultado = await apiFetch('/api/auth/convidar', {
            method: 'POST',
            body: JSON.stringify({
                email: document.getElementById('email').value,
                grupoId: document.getElementById('grupoId').value
            })
        });

        let mensagem = `Convite enviado para ${resultado.email}.`;
        if (!resultado.emailEnviado) {
            mensagem += ` O e-mail não pôde ser enviado — token de aceite: ${resultado.token}`;
        }

        sucessoDiv.textContent = mensagem;
        sucessoDiv.classList.remove('d-none');
        document.getElementById('form-convite').reset();

        await carregarUsuarios();
    } catch (err) {
        erroDiv.textContent = err.message;
        erroDiv.classList.remove('d-none');
    }
}

async function reenviarConvite(id) {
    try {
        const resultado = await apiFetch(`/api/auth/usuarios/${id}/reenviar-convite`, { method: 'POST' });

        let mensagem = `Convite reenviado para ${resultado.email}.`;
        if (!resultado.emailEnviado) {
            mensagem += ` O e-mail não pôde ser enviado — token de aceite: ${resultado.token}`;
        }

        mostrarToast(mensagem);
    } catch (err) {
        mostrarErro(err.message);
    }
}

function abrirModalGrupoUsuario(id, email, grupoIdAtual) {
    document.getElementById('erro-grupo-usuario').classList.add('d-none');
    document.getElementById('grupo-usuarioId').value = id;
    document.getElementById('grupo-usuario-email').textContent = email;
    document.getElementById('grupo-usuario-novo').value = grupoIdAtual;
    modalGrupoUsuario.show();
}

async function salvarGrupoUsuario(e) {
    e.preventDefault();
    const erroDiv = document.getElementById('erro-grupo-usuario');
    erroDiv.classList.add('d-none');

    const id = document.getElementById('grupo-usuarioId').value;
    const grupoId = document.getElementById('grupo-usuario-novo').value;

    try {
        await apiFetch(`/api/auth/usuarios/${id}/grupo`, {
            method: 'PUT',
            body: JSON.stringify({ grupoId })
        });

        modalGrupoUsuario.hide();
        await carregarUsuarios();
        mostrarToast('Grupo atualizado.');
    } catch (err) {
        erroDiv.textContent = err.message;
        erroDiv.classList.remove('d-none');
    }
}

async function resetarSenha(id, email) {
    const confirmado = await confirmarAcao(
        `Resetar a senha de ${email}? A senha atual dele deixa de funcionar na hora e um e-mail é enviado com o link para definir uma nova.`,
        { titulo: 'Resetar senha', textoBotao: 'Resetar', variante: 'perigo' }
    );
    if (!confirmado) return;

    try {
        const resultado = await apiFetch(`/api/auth/usuarios/${id}/resetar-senha`, { method: 'POST' });

        let mensagem = `Senha resetada — enviamos um link para ${resultado.email} definir uma nova.`;
        if (!resultado.emailEnviado) {
            mensagem += ` O e-mail não pôde ser enviado — token: ${resultado.token}`;
        }

        mostrarToast(mensagem);
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function bloquearUsuario(id) {
    const confirmado = await confirmarAcao(
        'Bloquear o acesso deste usuário? Ele não conseguirá mais fazer login até ser desbloqueado.',
        { titulo: 'Bloquear usuário', textoBotao: 'Bloquear', variante: 'perigo' }
    );
    if (!confirmado) return;

    try {
        await apiFetch(`/api/auth/usuarios/${id}/bloquear`, { method: 'POST' });
        await carregarUsuarios();
        mostrarToast('Usuário bloqueado.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function desbloquearUsuario(id) {
    try {
        await apiFetch(`/api/auth/usuarios/${id}/desbloquear`, { method: 'POST' });
        await carregarUsuarios();
        mostrarToast('Usuário desbloqueado.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function excluirUsuario(id) {
    const confirmado = await confirmarAcao(
        'Excluir este usuário? A ação não pode ser desfeita.',
        { titulo: 'Excluir usuário', textoBotao: 'Excluir', variante: 'perigo' }
    );
    if (!confirmado) return;

    try {
        await apiFetch(`/api/auth/usuarios/${id}`, { method: 'DELETE' });
        await carregarUsuarios();
        mostrarToast('Usuário excluído.');
    } catch (err) {
        mostrarErro(err.message);
    }
}
