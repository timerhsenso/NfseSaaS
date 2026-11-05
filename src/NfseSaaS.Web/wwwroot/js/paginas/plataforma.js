// Painel do operador da plataforma — todas as contas (tenants).
let tabelaTenants;
let tabelaPremios;
let modalSuspender;
let tenantParaSuspender = null;

function formatarCnpjPlataforma(cnpj) {
    const d = (cnpj || '').replace(/\D/g, '');
    return d.length === 14 ? d.replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5') : cnpj;
}

// StatusAssinatura (Domain.Enums): 1 Teste, 2 Ativa, 3 Atrasada, 4 Cancelada.
function situacaoCobrancaPlataforma(t) {
    const preco = t.precoMensal != null ? ` · R$ ${Number(t.precoMensal).toFixed(2).replace('.', ',')}` : '';
    switch (t.statusAssinatura) {
        case 2: return `<div class="small text-muted mt-1">Assinante ${escaparHtml(t.plano ?? '')}${preco}</div>`;
        case 3: return `<div class="small text-danger mt-1">Fatura em atraso · ${escaparHtml(t.plano ?? '')}</div>`;
        case 4: return '<div class="small text-muted mt-1">Assinatura cancelada</div>';
        case 1: return `<div class="small text-muted mt-1">${t.testeIniciadoEm ? 'Em teste desde ' + dataCurta(t.testeIniciadoEm) : 'Teste não iniciado'}</div>`;
        default: return '';
    }
}

// StatusPremioIndicacao (Domain.Enums): 1 Pendente, 2 Aplicando, 3 Aplicado, 4 Erro.
function situacaoPremioPlataforma(status) {
    switch (status) {
        case 1: return '<span class="badge text-bg-warning">Guardado</span>';
        case 2: return '<span class="badge text-bg-info">Aplicando</span>';
        case 3: return '<span class="badge text-bg-success">Aplicado</span>';
        case 4: return '<span class="badge text-bg-danger">Erro</span>';
        default: return '';
    }
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
                    `<div class="small text-muted">${escaparHtml(formatarCnpjPlataforma(t.cnpj))}${t.nomeFantasia ? ' · ' + escaparHtml(v) : ''}</div>` +
                    (t.indicadaPor || t.origemCadastro || t.indicacoes
                        ? `<div class="small text-muted">${[
                            t.indicadaPor ? `indicada por ${escaparHtml(t.indicadaPor)}` : (t.origemCadastro ? `origem: ${escaparHtml(t.origemCadastro)}` : ''),
                            t.indicacoes ? `<i class="bi bi-gift" aria-hidden="true"></i> ${t.indicacoes} indicação(ões)` : ''
                          ].filter(Boolean).join(' · ')}</div>`
                        : '')
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
                    const cobranca = situacaoCobrancaPlataforma(t);
                    if (v) return '<span class="badge text-bg-success">Ativa</span>' + cobranca;
                    return `<span class="badge text-bg-danger">Suspensa</span>` +
                        `<div class="small text-muted mt-1">${dataCurta(t.bloqueadoEm)}${t.motivoBloqueio ? ' · ' + escaparHtml(t.motivoBloqueio) : ''}</div>` + cobranca;
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

    tabelaPremios = new DataTable('#tabela-premios', {
        order: [[0, 'desc']],
        pageLength: 10,
        columns: [
            { data: 'criadoEm', render: (v, type) => type === 'display' ? dataCurta(v) : v },
            {
                data: 'conta',
                render: (v, type, p) => type !== 'display' ? v :
                    `${escaparHtml(v)}<div class="small text-muted">${p.papel === 1 ? 'a conta indicada' : 'quem indicou'}</div>`
            },
            { data: 'contaIndicada', render: (v, type) => type === 'display' ? escaparHtml(v) : v },
            {
                data: 'status',
                render: (v, type, p) => type !== 'display' ? v :
                    situacaoPremioPlataforma(v) + (p.aplicadoEm ? `<div class="small text-muted mt-1">${dataCurta(p.aplicadoEm)}</div>` : '')
            },
            {
                data: 'detalhe',
                render: (v, type, p) => type !== 'display' ? (v ?? '') :
                    `<span class="small">${escaparHtml(v ?? '')}</span>${p.tentativas > 1 ? `<div class="small text-muted">${p.tentativas} tentativa(s)</div>` : ''}`
            },
            {
                data: null, orderable: false,
                render: (d, type, p) => p.status === 4
                    ? `<button type="button" class="btn btn-sm btn-outline-primary btn-tentar-premio" data-id="${p.id}" data-nome="${escaparHtml(p.conta)}"><i class="bi bi-arrow-repeat"></i> Tentar de novo</button>`
                    : ''
            }
        ],
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    document.getElementById('tabela-premios').addEventListener('click', async function (e) {
        const botao = e.target.closest('.btn-tentar-premio');
        if (botao) await executarComBotaoDesabilitado(botao, () => tentarPremioNovamente(botao.dataset.id, botao.dataset.nome));
    });

    modalSuspender = new bootstrap.Modal(document.getElementById('modal-suspender-tenant'));
    document.getElementById('btn-atualizar-plataforma').addEventListener('click', function () {
        carregarTenants();
        carregarPremios();
    });
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

    document.getElementById('btn-reproteger-chaves').addEventListener('click', function () {
        executarComBotaoDesabilitado(this, reprotegerChaves);
    });

    carregarTenants();
    carregarPremios();
    carregarProtecaoChaves();
});

async function carregarPremios() {
    try {
        const premios = await apiFetch('/api/plataforma/premios-indicacao');
        tabelaPremios.clear().rows.add(premios).draw();
        const contar = s => premios.filter(p => p.status === s).length;
        const erros = contar(4);
        document.getElementById('resumo-premios').innerHTML =
            `${contar(3)} aplicado(s) · ${contar(1)} guardado(s)` + (erros ? ` · <span class="text-danger fw-semibold">${erros} com erro</span>` : '');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function tentarPremioNovamente(id, nome) {
    if (!await confirmarAcao(
        `Antes, abra o painel do Asaas e confira se a próxima fatura de ${nome} já foi removida ou o vencimento já foi adiado. ` +
        'Se já foi, NÃO tente de novo (a conta ganharia 2 meses). Tentar de novo agora?',
        { titulo: 'Tentar aplicar o prêmio de novo', textoBotao: 'Tentar de novo' })) return;

    try {
        await apiFetch(`/api/plataforma/premios-indicacao/${id}/tentar-novamente`, { method: 'POST' });
        mostrarToast('Prêmio devolvido para a fila. Atualize em alguns segundos para ver o resultado.');
        await carregarPremios();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function carregarProtecaoChaves() {
    try {
        const s = await apiFetch('/api/plataforma/protecao-chaves');
        document.getElementById('chaves-pasta').textContent = s.pasta || '(pasta padrão do ASP.NET)';
        document.getElementById('chaves-mecanismo').innerHTML = s.chavesCriptografadas
            ? `<span class="badge text-bg-success">Criptografadas</span> <span class="text-muted">${escaparHtml(s.mecanismo)}</span>`
            : '<span class="badge text-bg-danger">Sem criptografia</span>';
        document.getElementById('chaves-total').innerHTML = s.chavesSemCriptografia > 0
            ? `${s.chavesTotal} <span class="badge text-bg-warning">${s.chavesSemCriptografia} sem criptografia</span>`
            : `${s.chavesTotal}`;
        document.getElementById('chaves-certificados').textContent = s.certificados;

        const orientacao = document.getElementById('chaves-orientacao');
        orientacao.textContent = s.orientacao;
        orientacao.className = 'alert mb-0 small ' + (s.chavesCriptografadas && s.chavesSemCriptografia === 0
            ? 'alert-success'
            : s.podeReproteger ? 'alert-warning' : 'alert-danger');

        document.getElementById('btn-reproteger-chaves').classList.toggle('d-none', !s.podeReproteger);
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function reprotegerChaves() {
    if (!await confirmarAcao(
        'Os certificados de todas as empresas serão regravados com uma chave nova criptografada, e as chaves antigas sem criptografia serão apagadas. ' +
        'Rode um backup manual antes. Na próxima reinicialização do sistema, todos precisarão entrar de novo.',
        { titulo: 'Proteger chaves antigas', textoBotao: 'Proteger agora', variante: 'perigo' })) return;

    try {
        const r = await apiFetch('/api/plataforma/protecao-chaves/reproteger', { method: 'POST' });
        mostrarToast(`Pronto: ${r.certificadosReprotegidos} certificado(s) regravado(s) e ${r.chavesRemovidas} chave(s) antiga(s) apagada(s).`);
        await carregarProtecaoChaves();
    } catch (err) {
        mostrarErro(err.message);
    }
}

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
