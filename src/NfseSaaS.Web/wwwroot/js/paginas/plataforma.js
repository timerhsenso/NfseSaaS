// Painel do operador da plataforma — todas as contas (tenants).
let tabelaTenants;
let tabelaPremios;
let modalSuspender;
let modalEncerrar;
let tenantParaEncerrar = null;
let tenantParaSuspender = null;
let modalExcluir;
let tenantParaExcluir = null;
let relogioExclusao = null;
let modalCobranca;
let acaoCobranca = null; // { tipo: 'prorrogar' | 'liberar' | 'isentar', id }

// CPF (contador pessoa física) ou CNPJ do titular da conta.
function formatarDocumentoPlataforma(documento) {
    const d = (documento || '').replace(/\D/g, '');
    if (d.length === 14) return d.replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5');
    if (d.length === 11) return d.replace(/(\d{3})(\d{3})(\d{3})(\d{2})/, '$1.$2.$3-$4');
    return documento;
}

// DateOnly ("2027-01-31") sem passar por Date (evita mostrar o dia anterior).
function diaCurto(iso) {
    if (!iso) return '—';
    const [ano, mes, dia] = iso.split('-');
    return `${dia}/${mes}/${ano}`;
}

function reaisPlataforma(v) {
    return `R$ ${Number(v).toFixed(2).replace('.', ',')}`;
}

// Categoria da conta para o filtro e os números de venda.
// StatusAssinatura (Domain.Enums): 1 Teste, 2 Ativa, 3 Atrasada, 4 Cancelada.
function categoriaConta(t) {
    const c = t.cobranca || {};
    if (t.encerradaEm) return 'encerrada';
    if (!t.ativo) return 'suspensa';
    if (c.isenta) return 'isenta';
    switch (t.statusAssinatura) {
        case 2: return 'assinante';
        case 3: return 'atraso';
        case 4: return 'cancelada';
        default:
            if (c.semTesteGratis && !c.temAssinaturaNoGateway) return 'sem-teste';
            if (c.temAssinaturaNoGateway) return 'teste'; // assinou no teste, primeira fatura ainda não paga
            if (!t.testeIniciadoEm) return 'nao-iniciado';
            return c.testeTerminaEm && new Date(c.testeTerminaEm) <= new Date() ? 'teste-acabado' : 'teste';
    }
}

function situacaoCobrancaPlataforma(t) {
    const c = t.cobranca || {};
    const preco = t.precoMensal != null ? ` · ${reaisPlataforma(t.precoMensal)}` : '';
    const selos = [];
    if (c.isenta) selos.push(`<span class="badge text-bg-secondary">Isenta${c.isentaNaConfiguracao ? ' (VPS)' : c.isentaAte ? ' até ' + diaCurto(c.isentaAte) : ''}</span>`);
    if (c.fundador) selos.push('<span class="badge text-bg-warning">Fundador</span>');
    else if (c.fundadorReservado) selos.push('<span class="badge text-bg-light border">Fundador reservado</span>');
    if (c.testeDiasExtras) selos.push(`<span class="badge text-bg-light border">+${c.testeDiasExtras} dia(s)</span>`);

    let texto;
    switch (categoriaConta(t)) {
        case 'assinante': texto = `Assinante ${escaparHtml(t.plano ?? '')}${preco}`; break;
        case 'atraso': texto = `<span class="text-danger">Fatura em atraso · ${escaparHtml(t.plano ?? '')}</span>`; break;
        case 'cancelada': texto = 'Assinatura cancelada'; break;
        case 'sem-teste': texto = '<span class="text-danger">Sem teste: CNPJ já usou em outra conta</span>'; break;
        case 'teste-acabado': texto = `<span class="text-danger">Teste acabou em ${dataCurta(c.testeTerminaEm)}</span>`; break;
        case 'nao-iniciado': texto = 'Teste não iniciado'; break;
        case 'teste': texto = c.temAssinaturaNoGateway
            ? `Assinou no teste · ${escaparHtml(t.plano ?? '')}`
            : `Em teste até ${dataCurta(c.testeTerminaEm)}`; break;
        default: texto = '';
    }
    return `<div class="small text-muted mt-1">${texto}</div>` + (selos.length ? `<div class="mt-1 d-flex flex-wrap gap-1">${selos.join('')}</div>` : '');
}

// Itens de cobrança do "⋯" da linha: só o que vale para a situação da conta.
function itensCobranca(t, nome) {
    const c = t.cobranca || {};
    const item = (acao, icone, texto) => ({ classe: 'btn-cobranca', icone, texto, dados: { acao, id: t.id, nome } });
    // Liberar nova exportação dos dados (Meus dados): vale para qualquer conta.
    const exportacao = item('liberar-exportacao', 'bi-file-earmark-zip', 'Liberar nova exportação dos dados');
    if (c.isentaNaConfiguracao) return [exportacao];

    // Já assinou: teste e isenção não se aplicam (faturas no Asaas); só
    // conferir a situação com o Asaas agora, sem esperar a conciliação diária.
    if (c.temAssinaturaNoGateway)
        return [item('conciliar', 'bi-arrow-repeat', 'Conferir com o Asaas agora'), { divisor: true }, exportacao];

    const podeTeste = t.statusAssinatura !== 4 && !c.isenta;
    return [
        podeTeste && !c.semTesteGratis && item('prorrogar', 'bi-calendar-plus', 'Prorrogar teste…'),
        podeTeste && item('liberar', 'bi-arrow-counterclockwise', 'Liberar novo teste…'),
        c.isenta ? item('remover-isencao', 'bi-x-circle', 'Remover isenção') : item('isentar', 'bi-gift', 'Isentar de cobrança…'),
        { divisor: true },
        exportacao
    ];
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
                render: (v, type, t) => type !== 'display' ? `${v} ${t.nomeFantasia ?? ''} ${t.documento}${t.perfil === 2 ? ' contador' : ''}` :
                    `<div class="fw-semibold">${escaparHtml(t.nomeFantasia || v)}` +
                    // PerfilConta (Domain.Enums): 1 Empresa, 2 Contador.
                    (t.perfil === 2 ? ' <span class="badge text-bg-info align-middle">Contador</span>' : '') + '</div>' +
                    `<div class="small text-muted">${escaparHtml(formatarDocumentoPlataforma(t.documento))}${t.nomeFantasia ? ' · ' + escaparHtml(v) : ''}</div>` +
                    (t.indicadaPor || t.origemCadastro || t.indicacoes
                        ? `<div class="small text-muted">${[
                            t.indicadaPor ? `indicada por ${escaparHtml(t.indicadaPor)}` : (t.origemCadastro ? `origem: ${escaparHtml(t.origemCadastro)}` : ''),
                            t.indicacoes ? `<i class="bi bi-gift" aria-hidden="true"></i> ${t.indicacoes} indicação(ões)` : ''
                          ].filter(Boolean).join(' · ')}</div>`
                        : '')
            },
            {
                data: 'emailResponsavel',
                render: (v, type, t) => type !== 'display' ? `${v ?? ''} ${t.whatsApp ?? ''}` :
                    (v ? escaparHtml(v) : '—') +
                    (t.whatsApp
                        ? `<div class="small"><a href="${escaparHtml(t.linkWhatsApp)}" target="_blank" rel="noopener" title="Conversar no WhatsApp">` +
                          `<i class="bi bi-whatsapp" aria-hidden="true"></i> ${escaparHtml(t.whatsApp)}</a>` +
                          (t.whatsAppAutorizado ? '' : ' <span class="text-muted" title="Não autorizou receber avisos por WhatsApp">(só contato)</span>') + '</div>'
                        : '')
            },
            { data: 'criadoEm', className: 'col-curta', render: (v, type) => type === 'display' ? dataCurta(v) : v },
            { data: 'usuarios', className: 'text-end col-valor' },
            {
                data: 'empresas', className: 'text-end col-valor',
                render: (v, type, t) => type === 'display'
                    ? `${v}${t.empresasEmProducao ? `<div class="small text-muted">${t.empresasEmProducao} em produção</div>` : ''}`
                    : v
            },
            {
                data: 'notasNoMes', className: 'text-end col-valor',
                render: (v, type, t) => type === 'display' ? `${v}<div class="small text-muted">${t.notasTotal} no total (produção)</div>` : v
            },
            { data: 'ultimaNota', className: 'col-curta', render: (v, type) => type === 'display' ? dataCurta(v) : (v ?? '') },
            {
                data: 'ativo', className: 'col-curta',
                render: (v, type, t) => {
                    if (type !== 'display') return (t.encerradaEm ? 'Encerrada ' : v ? 'Ativa ' : 'Suspensa ') + categoriaConta(t);
                    const cobranca = situacaoCobrancaPlataforma(t);
                    if (t.encerradaEm) {
                        const quando = t.dadosExpurgadosEm
                            ? `dados apagados em ${dataCurta(t.dadosExpurgadosEm)}`
                            : `apaga os dados em ${diaCurto(t.expurgoPrevistoEm)}`;
                        return '<span class="badge text-bg-dark">Encerrada</span>' +
                            `<div class="small text-muted mt-1">${dataCurta(t.encerradaEm)} · ${quando}</div>` +
                            (t.motivoBloqueio ? `<div class="small text-muted">${escaparHtml(t.motivoBloqueio)}</div>` : '');
                    }
                    if (v) return '<span class="badge text-bg-success">Ativa</span>' + cobranca;
                    return `<span class="badge text-bg-danger">Suspensa</span>` +
                        `<div class="small text-muted mt-1">${dataCurta(t.bloqueadoEm)}${t.motivoBloqueio ? ' · ' + escaparHtml(t.motivoBloqueio) : ''}</div>` + cobranca;
                }
            },
            {
                data: null, orderable: false,
                render: (d, type, t) => {
                    const nome = t.nomeFantasia || t.razaoSocial;
                    // Encerrada com dados já apagados: não há mais o que fazer pela tela.
                    if (t.dadosExpurgadosEm) return '';
                    const dados = { id: t.id, nome };
                    const principal = t.ativo
                        ? { classe: 'btn-suspender', icone: 'bi-pause-circle', texto: 'Suspender', cor: 'danger', mostrarTexto: true, dados }
                        : { classe: 'btn-reativar', icone: 'bi-play-circle', texto: t.encerradaEm ? 'Reabrir' : 'Reativar', cor: 'success', mostrarTexto: true, dados: { ...dados, encerrada: t.encerradaEm ? '1' : '' } };
                    return acoesDaLinha([principal], [
                        ...(t.encerradaEm ? [] : itensCobranca(t, nome)),
                        { divisor: true },
                        !t.encerradaEm && { classe: 'btn-encerrar-tenant', icone: 'bi-door-closed', texto: 'Encerrar a conta (apaga os dados em 90 dias)', dados },
                        // Só aparece para conta que nunca teve nota (a regra completa é conferida no servidor).
                        t.notasTotal === 0 && { classe: 'btn-excluir-tenant', icone: 'bi-trash', texto: 'Excluir definitivamente', perigo: true, dados }
                    ]);
                }
            }
        ],
    });

    // Filtro por situação (só na tabela de contas).
    DataTable.ext.search.push(function (settings, dados, indice) {
        if (settings.nTable.id !== 'tabela-tenants') return true;
        const filtro = document.getElementById('filtro-situacao').value;
        if (!filtro) return true;
        const t = tabelaTenants.row(indice).data();
        return categoriaConta(t) === filtro;
    });
    document.getElementById('filtro-situacao').addEventListener('change', () => tabelaTenants.draw());

    tabelaPremios = new DataTable('#tabela-premios', {
        order: [[0, 'desc']],
        pageLength: 10,
        columns: [
            { data: 'criadoEm', className: 'col-curta', render: (v, type) => type === 'display' ? dataCurta(v) : v },
            {
                data: 'conta',
                render: (v, type, p) => type !== 'display' ? v :
                    `${escaparHtml(v)}<div class="small text-muted">${p.papel === 1 ? 'a conta indicada' : 'quem indicou'}</div>`
            },
            { data: 'contaIndicada', render: (v, type) => type === 'display' ? escaparHtml(v) : v },
            {
                data: 'status', className: 'col-curta',
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
    });

    document.getElementById('tabela-premios').addEventListener('click', async function (e) {
        const botao = e.target.closest('.btn-tentar-premio');
        if (botao) await executarComBotaoDesabilitado(botao, () => tentarPremioNovamente(botao.dataset.id, botao.dataset.nome));
    });

    modalSuspender = new bootstrap.Modal(document.getElementById('modal-suspender-tenant'));
    modalCobranca = new bootstrap.Modal(document.getElementById('modal-cobranca-tenant'));
    document.getElementById('form-cobranca-tenant').addEventListener('submit', confirmarCobranca);
    modalExcluir = new bootstrap.Modal(document.getElementById('modal-excluir-tenant'));
    document.getElementById('form-excluir-tenant').addEventListener('submit', confirmarExclusaoTenant);
    document.getElementById('modal-excluir-tenant').addEventListener('hidden.bs.modal', pararRelogioExclusao);
    document.getElementById('btn-atualizar-plataforma').addEventListener('click', function () {
        carregarTenants();
        carregarPremios();
    });
    document.getElementById('form-suspender-tenant').addEventListener('submit', suspenderTenant);
    modalEncerrar = new bootstrap.Modal(document.getElementById('modal-encerrar-tenant'));
    document.getElementById('form-encerrar-tenant').addEventListener('submit', encerrarTenant);

    document.getElementById('tabela-tenants').addEventListener('click', async function (e) {
        const botao = e.target.closest('button');
        if (!botao) return;
        if (botao.classList.contains('btn-cobranca')) {
            await abrirCobranca(botao.dataset.acao, botao.dataset.id, botao.dataset.nome);
            return;
        }
        if (botao.classList.contains('btn-suspender')) {
            tenantParaSuspender = botao.dataset.id;
            document.getElementById('nome-tenant-suspender').textContent = botao.dataset.nome;
            document.getElementById('motivo-suspensao').value = '';
            ocultarErroFormulario('erro-suspender-tenant');
            modalSuspender.show();
        } else if (botao.classList.contains('btn-encerrar-tenant')) {
            tenantParaEncerrar = botao.dataset.id;
            document.getElementById('nome-tenant-encerrar').textContent = botao.dataset.nome;
            document.getElementById('motivo-encerramento').value = '';
            ocultarErroFormulario('erro-encerrar-tenant');
            modalEncerrar.show();
        } else if (botao.classList.contains('btn-excluir-tenant')) {
            await executarComBotaoDesabilitado(botao, () => pedirCodigoExclusao(botao.dataset.id, botao.dataset.nome));
        } else if (botao.classList.contains('btn-reativar')) {
            await executarComBotaoDesabilitado(botao, () => reativarTenant(botao.dataset.id, botao.dataset.nome, botao.dataset.encerrada === '1'));
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
        atualizarNumerosVenda(tenants);
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

async function encerrarTenant(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-encerrar-tenant');
    const motivo = document.getElementById('motivo-encerramento').value.trim();
    if (!motivo) {
        mostrarErroFormulario('erro-encerrar-tenant', 'Informe o motivo do encerramento.');
        return;
    }
    try {
        const r = await apiFetch(`/api/plataforma/tenants/${tenantParaEncerrar}/encerrar`, {
            method: 'POST', body: JSON.stringify({ motivo })
        });
        modalEncerrar.hide();
        mostrarToast(`Conta encerrada. Os dados serão apagados em ${diaCurto(r.expurgoPrevistoEm)}.`);
        await carregarTenants();
    } catch (err) {
        mostrarErroFormulario('erro-encerrar-tenant', err.message);
    }
}

async function reativarTenant(id, nome, encerrada) {
    const texto = encerrada
        ? `Reabrir a conta ${nome}? O encerramento é desfeito (os dados não serão mais apagados) e os usuários voltam a entrar. A assinatura continua cancelada: a conta precisa assinar de novo.`
        : `Reativar a conta ${nome}? Os usuários voltam a entrar e a emitir notas.`;
    if (!await confirmarAcao(texto, { titulo: encerrada ? 'Reabrir conta' : 'Reativar conta', textoBotao: encerrada ? 'Reabrir' : 'Reativar' })) return;

    try {
        await apiFetch(`/api/plataforma/tenants/${id}/reativar`, { method: 'POST' });
        mostrarToast(encerrada ? 'Conta reaberta.' : 'Conta reativada.');
        await carregarTenants();
    } catch (err) {
        mostrarErro(err.message);
    }
}

// ---------- Excluir conta (2 passos: código por e-mail e confirmação) ----------

async function pedirCodigoExclusao(id, nome) {
    if (!await confirmarAcao(
        `Excluir DEFINITIVAMENTE a conta ${nome}? Empresas, clientes, contratos, usuários e todo o resto serão apagados, sem volta. ` +
        'Vamos enviar um código para o seu e-mail para confirmar.',
        { titulo: 'Excluir conta', textoBotao: 'Enviar código', variante: 'perigo' })) return;

    try {
        const r = await apiFetch(`/api/plataforma/tenants/${id}/exclusao/codigo`, { method: 'POST' });
        tenantParaExcluir = id;
        document.getElementById('nome-tenant-excluir').textContent = nome;
        document.getElementById('email-codigo-exclusao').textContent = r.emailMascarado;
        document.getElementById('resumo-exclusao-tenant').textContent =
            `${r.empresas} empresa(s), ${r.clientes} cliente(s), ${r.contratos} contrato(s), ${r.servicos} serviço(s) e ${r.usuarios} usuário(s)`;
        document.getElementById('codigo-exclusao').value = '';
        ocultarErroFormulario('erro-excluir-tenant');
        iniciarRelogioExclusao(r.minutosValidade);
        modalExcluir.show();
        setTimeout(() => document.getElementById('codigo-exclusao').focus(), 300);
    } catch (err) {
        mostrarErro(err.message);
    }
}

function iniciarRelogioExclusao(minutos) {
    pararRelogioExclusao();
    const fim = Date.now() + minutos * 60 * 1000;
    const texto = document.getElementById('tempo-codigo-exclusao');
    const botao = document.getElementById('btn-confirmar-exclusao');
    botao.disabled = false;
    const atualizar = () => {
        const restante = Math.max(0, Math.round((fim - Date.now()) / 1000));
        if (restante === 0) {
            texto.textContent = 'O código expirou. Feche e clique em excluir de novo para receber outro.';
            botao.disabled = true;
            pararRelogioExclusao();
            return;
        }
        texto.textContent = `O código vale por mais ${Math.floor(restante / 60)}:${String(restante % 60).padStart(2, '0')}.`;
    };
    atualizar();
    relogioExclusao = setInterval(atualizar, 1000);
}

function pararRelogioExclusao() {
    if (relogioExclusao) clearInterval(relogioExclusao);
    relogioExclusao = null;
}

async function confirmarExclusaoTenant(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-excluir-tenant');
    const codigo = document.getElementById('codigo-exclusao').value.trim();
    if (!/^[0-9]{6}$/.test(codigo)) {
        mostrarErroFormulario('erro-excluir-tenant', 'Digite os 6 números do código.');
        return;
    }

    const botao = document.getElementById('btn-confirmar-exclusao');
    await executarComBotaoDesabilitado(botao, async () => {
        try {
            await apiFetch(`/api/plataforma/tenants/${tenantParaExcluir}/exclusao/confirmar`, {
                method: 'POST', body: JSON.stringify({ codigo })
            });
            modalExcluir.hide();
            mostrarToast('Conta excluída definitivamente.');
            await carregarTenants();
            await carregarPremios();
        } catch (err) {
            mostrarErroFormulario('erro-excluir-tenant', err.message);
        }
    });
}

// ---------- Números de venda ----------

async function atualizarNumerosVenda(tenants) {
    const por = cat => tenants.filter(t => categoriaConta(t) === cat);
    const emTeste = por('teste');
    const acabados = por('teste-acabado');
    const assinantes = por('assinante');
    const atraso = por('atraso');
    const pagantes = assinantes.concat(atraso);

    document.getElementById('kpi-em-teste').textContent = emTeste.length;
    document.getElementById('kpi-em-teste-detalhe').textContent =
        `${acabados.length} acabaram sem assinar · ${por('nao-iniciado').length} sem nota em produção`;

    document.getElementById('kpi-assinantes').textContent = pagantes.length;
    document.getElementById('kpi-assinantes-detalhe').textContent =
        `${atraso.length} em atraso · ${pagantes.filter(t => t.cobranca?.fundador).length} fundador(es)`;

    document.getElementById('kpi-mrr').textContent =
        reaisPlataforma(pagantes.reduce((soma, t) => soma + (t.precoMensal ?? 0), 0));

    // Conversão: quem é assinante hoje (em dia ou em atraso) sobre quem já começou o teste (sem as isentas).
    const comecaram = tenants.filter(t => t.testeIniciadoEm && !t.cobranca?.isenta);
    const converteram = comecaram.filter(t => [2, 3].includes(t.statusAssinatura));
    document.getElementById('kpi-conversao').textContent = comecaram.length
        ? `${Math.round(100 * converteram.length / comecaram.length)}%`
        : '—';
    document.getElementById('kpi-conversao-detalhe').textContent =
        `${converteram.length} de ${comecaram.length} que começaram o teste`;

    try {
        const vagas = await apiFetch('/api/plataforma/vagas-fundador');
        // PublicoPlano (Domain.Cobranca): 1 Empresas, 2 Escritorios.
        const grupo = p => vagas.find(v => v.publico === p);
        const empresas = grupo(1), escritorios = grupo(2);
        document.getElementById('kpi-vagas').textContent = (empresas?.restantes ?? 0) + (escritorios?.restantes ?? 0);
        document.getElementById('kpi-vagas-detalhe').textContent =
            `empresas ${empresas?.restantes ?? 0} · escritórios ${escritorios?.restantes ?? 0}` +
            ((empresas?.reservas ?? 0) + (escritorios?.reservas ?? 0) ? ` · ${(empresas?.reservas ?? 0) + (escritorios?.reservas ?? 0)} reservada(s)` : '');
    } catch {
        document.getElementById('kpi-vagas').textContent = '—';
    }
}

// ---------- Cobrança da conta (prorrogar, liberar teste, isentar) ----------

const TEXTOS_COBRANCA = {
    prorrogar: {
        titulo: 'Prorrogar teste grátis',
        explicacao: 'Soma dias ao teste desta conta (também estende a vaga de fundador reservada). Só para conta sem assinatura.',
        botao: 'Prorrogar', dias: true, ate: false
    },
    liberar: {
        titulo: 'Liberar novo teste',
        explicacao: 'Começa um teste novo de 30 dias a partir de hoje, inclusive para conta sem teste porque o CNPJ já tinha usado em outra conta.',
        botao: 'Liberar teste', dias: false, ate: false
    },
    isentar: {
        titulo: 'Isentar de cobrança',
        explicacao: 'A conta emite em produção sem teste e sem assinatura até a data (ou sem prazo). Se ela assinar, a isenção acaba e a primeira fatura vence no dia seguinte ao fim da isenção.',
        botao: 'Isentar', dias: false, ate: true
    }
};

async function abrirCobranca(acao, id, nome) {
    if (acao === 'conciliar') {
        try {
            mostrarToast(`Conferindo ${nome} com o Asaas…`);
            const r = await apiFetch(`/api/plataforma/tenants/${id}/cobranca/conciliar`, { method: 'POST' });
            mostrarToast(r.mensagem);
            if (r.mudou) await carregarTenants();
        } catch (err) {
            mostrarErro(err.message);
        }
        return;
    }

    if (acao === 'liberar-exportacao') {
        if (!await confirmarAcao(`Liberar uma nova exportação dos dados para ${nome}? A exportação pronta atual deixa de valer (o arquivo é apagado) e a conta pode pedir outra agora.`,
            { titulo: 'Liberar nova exportação', textoBotao: 'Liberar' })) return;
        try {
            await apiFetch(`/api/plataforma/tenants/${id}/exportacao/liberar`, { method: 'POST' });
            mostrarToast('Pronto: a conta já pode pedir uma nova exportação em Meus dados.');
        } catch (err) {
            mostrarErro(err.message);
        }
        return;
    }

    if (acao === 'remover-isencao') {
        if (!await confirmarAcao(`Remover a isenção de ${nome}? A conta volta às regras normais (teste ou assinatura).`,
            { titulo: 'Remover isenção', textoBotao: 'Remover' })) return;
        try {
            const r = await apiFetch(`/api/plataforma/tenants/${id}/isencao`, { method: 'DELETE' });
            mostrarToast(r.mensagem);
            await carregarTenants();
        } catch (err) {
            mostrarErro(err.message);
        }
        return;
    }

    const textos = TEXTOS_COBRANCA[acao];
    acaoCobranca = { tipo: acao, id };
    document.getElementById('titulo-cobranca-tenant').textContent = textos.titulo;
    document.getElementById('nome-tenant-cobranca').textContent = nome;
    document.getElementById('explicacao-cobranca-tenant').textContent = textos.explicacao;
    document.getElementById('grupo-dias-cobranca').classList.toggle('d-none', !textos.dias);
    document.getElementById('grupo-ate-cobranca').classList.toggle('d-none', !textos.ate);
    document.getElementById('btn-confirmar-cobranca').textContent = textos.botao;
    document.getElementById('dias-cobranca').value = 15;
    document.getElementById('ate-cobranca').value = '';
    document.getElementById('motivo-cobranca').value = '';
    ocultarErroFormulario('erro-cobranca-tenant');
    modalCobranca.show();
}

async function confirmarCobranca(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-cobranca-tenant');
    const motivo = document.getElementById('motivo-cobranca').value.trim();
    if (!motivo) {
        mostrarErroFormulario('erro-cobranca-tenant', 'Informe o motivo.');
        return;
    }

    const { tipo, id } = acaoCobranca;
    const chamada = {
        prorrogar: () => apiFetch(`/api/plataforma/tenants/${id}/teste/prorrogar`, {
            method: 'POST', body: JSON.stringify({ dias: Number(document.getElementById('dias-cobranca').value), motivo })
        }),
        liberar: () => apiFetch(`/api/plataforma/tenants/${id}/teste/liberar`, {
            method: 'POST', body: JSON.stringify({ motivo })
        }),
        isentar: () => apiFetch(`/api/plataforma/tenants/${id}/isencao`, {
            method: 'POST', body: JSON.stringify({ ate: document.getElementById('ate-cobranca').value || null, motivo })
        })
    }[tipo];

    const botao = document.getElementById('btn-confirmar-cobranca');
    await executarComBotaoDesabilitado(botao, async () => {
        try {
            const r = await chamada();
            modalCobranca.hide();
            mostrarToast(r.mensagem);
            await carregarTenants();
        } catch (err) {
            mostrarErroFormulario('erro-cobranca-tenant', err.message);
        }
    });
}
