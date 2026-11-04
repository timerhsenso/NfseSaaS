// Painel do operador da plataforma — todas as contas (tenants).
let tabelaTenants;
let modalSuspender;
let tenantParaSuspender = null;

function formatarCnpjPlataforma(cnpj) {
    const d = (cnpj || '').replace(/\D/g, '');
    return d.length === 14 ? d.replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5') : cnpj;
}

function dataCurta(iso) {
    return iso ? new Date(iso).toLocaleDateString('pt-BR') : '—';
}

document.addEventListener('DOMContentLoaded', function () {
    tabelaTenants = new DataTable('#tabela-tenants', {
        order: [[0, 'asc']],
        columns: [
            {
                data: 'razaoSocial',
                render: (v, type, t) => type !== 'display' ? `${v} ${t.nomeFantasia ?? ''} ${t.cnpj}` :
                    `<div class="fw-semibold">${escaparHtml(t.nomeFantasia || v)}</div>` +
                    `<div class="small text-muted">${escaparHtml(formatarCnpjPlataforma(t.cnpj))}${t.nomeFantasia ? ' · ' + escaparHtml(v) : ''}</div>`
            },
            { data: 'emailResponsavel', render: v => v ? escaparHtml(v) : '—' },
            { data: 'criadoEm', render: (v, type) => type === 'display' ? dataCurta(v) : v },
            { data: 'usuarios', className: 'text-end' },
            {
                data: 'empresas', className: 'text-end',
                render: (v, type, t) => type === 'display'
                    ? `${v}${t.empresasEmProducao ? `<div class="small text-muted">${t.empresasEmProducao} em produção</div>` : ''}`
                    : v
            },
            {
                data: 'notasNoMes', className: 'text-end',
                render: (v, type, t) => type === 'display' ? `${v}<div class="small text-muted">${t.notasTotal} no total</div>` : v
            },
            { data: 'ultimaNota', render: (v, type) => type === 'display' ? dataCurta(v) : (v ?? '') },
            {
                data: 'ativo',
                render: (v, type, t) => {
                    if (type !== 'display') return v ? 'Ativa' : 'Suspensa';
                    if (v) return '<span class="badge text-bg-success">Ativa</span>';
                    return `<span class="badge text-bg-danger">Suspensa</span>` +
                        `<div class="small text-muted mt-1">${dataCurta(t.bloqueadoEm)}${t.motivoBloqueio ? ' · ' + escaparHtml(t.motivoBloqueio) : ''}</div>`;
                }
            },
            {
                data: null, orderable: false,
                render: (d, type, t) => t.ativo
                    ? `<button type="button" class="btn btn-sm btn-outline-danger btn-suspender" data-id="${t.id}" data-nome="${escaparHtml(t.nomeFantasia || t.razaoSocial)}"><i class="bi bi-pause-circle"></i> Suspender</button>`
                    : `<button type="button" class="btn btn-sm btn-outline-success btn-reativar" data-id="${t.id}" data-nome="${escaparHtml(t.nomeFantasia || t.razaoSocial)}"><i class="bi bi-play-circle"></i> Reativar</button>`
            }
        ],
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    modalSuspender = new bootstrap.Modal(document.getElementById('modal-suspender-tenant'));
    document.getElementById('btn-atualizar-plataforma').addEventListener('click', carregarTenants);
    document.getElementById('form-suspender-tenant').addEventListener('submit', suspenderTenant);

    document.getElementById('tabela-tenants').addEventListener('click', async function (e) {
        const botao = e.target.closest('button');
        if (!botao) return;
        if (botao.classList.contains('btn-suspender')) {
            tenantParaSuspender = botao.dataset.id;
            document.getElementById('nome-tenant-suspender').textContent = botao.dataset.nome;
            document.getElementById('motivo-suspensao').value = '';
            ocultarErroFormulario('erro-suspender-tenant');
            modalSuspender.show();
        } else if (botao.classList.contains('btn-reativar')) {
            await executarComBotaoDesabilitado(botao, () => reativarTenant(botao.dataset.id, botao.dataset.nome));
        }
    });

    carregarTenants();
});

async function carregarTenants() {
    try {
        const tenants = await apiFetch('/api/plataforma/tenants');
        tabelaTenants.clear().rows.add(tenants).draw();

        const ativas = tenants.filter(t => t.ativo);
        document.getElementById('kpi-ativas').textContent = ativas.length;
        document.getElementById('kpi-ativas-detalhe').textContent = `${tenants.length} conta(s) no total`;
        document.getElementById('kpi-suspensas').textContent = tenants.length - ativas.length;
        document.getElementById('kpi-notas-mes').textContent = tenants.reduce((s, t) => s + t.notasNoMes, 0);
        document.getElementById('kpi-producao').textContent = tenants.reduce((s, t) => s + t.empresasEmProducao, 0);
        document.getElementById('kpi-producao-detalhe').textContent =
            `de ${tenants.reduce((s, t) => s + t.empresas, 0)} empresa(s) cadastradas`;
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function suspenderTenant(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-suspender-tenant');
    const motivo = document.getElementById('motivo-suspensao').value.trim();
    if (!motivo) {
        mostrarErroFormulario('erro-suspender-tenant', 'Informe o motivo da suspensão.');
        return;
    }

    try {
        await apiFetch(`/api/plataforma/tenants/${tenantParaSuspender}/suspender`, {
            method: 'POST', body: JSON.stringify({ motivo })
        });
        modalSuspender.hide();
        mostrarToast('Conta suspensa. As sessões abertas caem em até 1 minuto.');
        await carregarTenants();
    } catch (err) {
        mostrarErroFormulario('erro-suspender-tenant', err.message);
    }
}

async function reativarTenant(id, nome) {
    if (!await confirmarAcao(`Reativar a conta ${nome}? Os usuários voltam a entrar e a emitir notas.`,
        { titulo: 'Reativar conta', textoBotao: 'Reativar' })) return;

    try {
        await apiFetch(`/api/plataforma/tenants/${id}/reativar`, { method: 'POST' });
        mostrarToast('Conta reativada.');
        await carregarTenants();
    } catch (err) {
        mostrarErro(err.message);
    }
}
