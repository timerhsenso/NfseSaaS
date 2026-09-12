let tabelaEmails;
let modalErroEmail;

document.addEventListener('DOMContentLoaded', function () {
    tabelaEmails = new DataTable('#tabela-emails', {
        columns: [
            { data: 'destinatario' },
            { data: 'tipo', render: v => rotuloTipo(v) },
            { data: 'assunto' },
            { data: 'status', render: v => rotuloStatus(v) },
            { data: 'tentativas' },
            { data: 'createdAt', render: v => new Date(v).toLocaleString('pt-BR') },
            {
                data: null,
                orderable: false,
                render: (data, type, email) => {
                    let botoes = '';
                    if (email.status === 'Falhou') {
                        botoes += `
                            <button type="button" class="btn btn-sm btn-outline-secondary btn-ver-erro" data-erro="${(email.erroDetalhe || '').replace(/"/g, '&quot;')}" title="Ver detalhe do erro">
                                <i class="bi bi-exclamation-triangle"></i>
                            </button>
                        `;
                    }
                    if (email.status === 'Falhou' || email.status === 'Enviado') {
                        botoes += `
                            <button type="button" class="btn btn-sm btn-outline-primary btn-reenviar-email" data-id="${email.id}" title="Reenviar">
                                <i class="bi bi-arrow-repeat"></i>
                            </button>
                        `;
                    }
                    return botoes || '—';
                }
            }
        ],
        order: [[5, 'desc']],
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    modalErroEmail = new bootstrap.Modal(document.getElementById('modal-erro-email'));

    document.getElementById('tabela-emails').addEventListener('click', async function (e) {
        const botao = e.target.closest('button');
        if (!botao) return;

        if (botao.classList.contains('btn-ver-erro')) {
            document.getElementById('erro-email-detalhe').textContent = botao.dataset.erro || 'Sem detalhe registrado.';
            modalErroEmail.show();
        } else if (botao.classList.contains('btn-reenviar-email')) {
            await reenviarEmail(botao.dataset.id);
        }
    });

    document.getElementById('filtro-status').addEventListener('change', carregarEmails);
    document.getElementById('filtro-tipo').addEventListener('change', carregarEmails);
    document.getElementById('filtro-busca').addEventListener('input', debounce(carregarEmails, 400));

    carregarEmails();
});

function rotuloStatus(status) {
    switch (status) {
        case 'Pendente': return '<span class="badge text-bg-warning">Pendente</span>';
        case 'Enviado': return '<span class="badge text-bg-success">Enviado</span>';
        case 'Falhou': return '<span class="badge text-bg-danger">Falhou</span>';
        default: return status;
    }
}

function rotuloTipo(tipo) {
    switch (tipo) {
        case 'Convite': return 'Convite';
        case 'ResetSenha': return 'Reset de senha (Admin)';
        case 'EsqueciSenha': return 'Esqueci minha senha';
        case 'AvisoTrocaSenha': return 'Aviso de troca de senha';
        default: return tipo;
    }
}

async function carregarEmails() {
    const params = new URLSearchParams({ pageSize: '200' });

    const status = document.getElementById('filtro-status').value;
    const tipo = document.getElementById('filtro-tipo').value;
    const busca = document.getElementById('filtro-busca').value;

    if (status) params.set('status', status);
    if (tipo) params.set('tipo', tipo);
    if (busca) params.set('busca', busca);

    try {
        const resultado = await apiFetch(`/api/emails?${params.toString()}`);
        tabelaEmails.clear();
        tabelaEmails.rows.add(resultado.items);
        tabelaEmails.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function reenviarEmail(id) {
    try {
        await apiFetch(`/api/emails/${id}/reenviar`, { method: 'POST' });
        await carregarEmails();
        mostrarToast('E-mail reenfileirado para envio.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

function debounce(fn, atrasoMs) {
    let timer;
    return function (...args) {
        clearTimeout(timer);
        timer = setTimeout(() => fn.apply(this, args), atrasoMs);
    };
}
