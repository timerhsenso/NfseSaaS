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
            <td>${n.numeroNfse ? `<strong>${escaparHtml(n.numeroNfse)}</strong>` : `<span class="text-muted">DPS ${n.numeroDps}</span>`}</td>
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

        let vencendo = 0, vencidos = 0, aguardandoAprovacao = 0;
        respostas.forEach(r => {
            (r?.items ?? []).forEach(c => {
                if (c.reajustePendenteId) aguardandoAprovacao++;
                if (c.situacao === 1) vencendo++;
                else if (c.situacao === 2) vencidos++;
            });
        });

        elemento.textContent = vencendo + vencidos;
        detalhe.textContent = aguardandoAprovacao > 0
            ? `${aguardandoAprovacao} aguardando sua aprovação` + (vencidos - aguardandoAprovacao > 0 ? `, ${vencidos - aguardandoAprovacao} vencido(s)` : '') + (vencendo > 0 ? `, ${vencendo} vencendo` : '')
            : vencidos > 0
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

// "Primeiros passos": some sozinho quando a conta completa todos os passos.
// "Ocultar por agora" vale só neste navegador e volta quando um passo novo
// for concluído (a chave guarda quantos estavam concluídos ao ocultar).
const CHAVE_ONBOARDING_OCULTO = 'micoempresa.onboarding.ocultoEm';

function lerOcultoEm() {
    try { return parseInt(localStorage.getItem(CHAVE_ONBOARDING_OCULTO) ?? '', 10); } catch { return NaN; }
}

function gravarOcultoEm(valor) {
    try { localStorage.setItem(CHAVE_ONBOARDING_OCULTO, String(valor)); } catch { /* sem armazenamento: só não lembra */ }
}

async function carregarOnboarding() {
    const card = document.getElementById('card-onboarding');
    let progresso;
    try {
        progresso = await apiFetch('/api/onboarding');
    } catch {
        return; // o painel continua funcionando sem o "Primeiros passos"
    }

    if (!progresso || progresso.completo || lerOcultoEm() === progresso.concluidos) {
        card.classList.add('d-none');
        return;
    }

    const total = progresso.passos.length;
    document.getElementById('onboarding-contagem').textContent = `${progresso.concluidos} de ${total}`;
    const barra = document.getElementById('onboarding-barra');
    barra.style.width = `${Math.round(progresso.concluidos * 100 / total)}%`;
    barra.parentElement.setAttribute('aria-valuenow', String(Math.round(progresso.concluidos * 100 / total)));

    const proximaChave = progresso.proximo?.chave;
    document.getElementById('onboarding-passos').innerHTML = progresso.passos.map(p => {
        const proximo = p.chave === proximaChave;
        const icone = p.concluido
            ? '<i class="bi bi-check-circle-fill onboarding-ok" aria-hidden="true"></i>'
            : `<i class="bi ${proximo ? 'bi-arrow-right-circle-fill onboarding-proximo' : 'bi-circle onboarding-pendente'}" aria-hidden="true"></i>`;
        const acoes = proximo
            ? `<div class="onboarding-acoes">
                   <a href="${p.link}" class="btn btn-primary btn-sm">${escaparHtml(p.textoBotao)}</a>
                   ${p.linkAjuda ? `<a href="${p.linkAjuda}" target="_blank" rel="noopener" class="btn btn-link btn-sm">Como conseguir?</a>` : ''}
               </div>`
            : '';
        return `<li class="${p.concluido ? 'concluido' : ''} ${proximo ? 'atual' : ''}">
                    ${icone}
                    <div>
                        <strong>${escaparHtml(p.titulo)}</strong><span class="visually-hidden">${p.concluido ? ' (concluído)' : ''}</span>
                        ${proximo ? `<p>${escaparHtml(p.descricao)}</p>` : ''}
                        ${acoes}
                    </div>
                </li>`;
    }).join('');

    document.getElementById('btn-ocultar-onboarding').onclick = function () {
        gravarOcultoEm(progresso.concluidos);
        card.classList.add('d-none');
    };

    card.classList.remove('d-none');
}

// Lembrete do WhatsApp da conta: só para o administrador, enquanto a conta
// não tem número. "Agora não" adia 30 dias (no servidor, vale em qualquer
// navegador).
async function carregarLembreteWhatsApp() {
    const aviso = document.getElementById('lembrete-whatsapp');
    let dados;
    try {
        dados = await apiFetch('/api/conta/dados');
    } catch {
        return; // o painel continua funcionando sem o lembrete
    }
    if (!dados?.mostrarLembreteWhatsApp) return;

    document.getElementById('btn-adiar-lembrete-whatsapp').onclick = async function () {
        aviso.classList.add('d-none');
        try {
            await apiFetch('/api/conta/lembrete-whatsapp/adiar', { method: 'POST' });
        } catch { /* volta a aparecer na próxima visita */ }
    };
    aviso.classList.remove('d-none');
}

// DOC1: documentos da empresa vencidos ou vencendo em até 15 dias (todas as
// empresas da conta). Sem permissão de Empresas ou sem nada a avisar: nada aparece.
async function carregarAvisoDocumentos() {
    const aviso = document.getElementById('aviso-documentos');
    let resumo;
    try {
        resumo = await apiFetch('/api/documentos-empresa/resumo');
    } catch {
        return;
    }
    if (!resumo || (resumo.vencidos === 0 && resumo.venceEmBreve === 0)) return;

    const partes = [];
    if (resumo.vencidos > 0) partes.push(resumo.vencidos === 1 ? '1 documento vencido' : `${resumo.vencidos} documentos vencidos`);
    if (resumo.venceEmBreve > 0) partes.push(`${resumo.venceEmBreve} ${resumo.vencidos > 0 ? '' : (resumo.venceEmBreve === 1 ? 'documento ' : 'documentos ')}vencendo em até 15 dias`);
    const empresas = new Set(resumo.itens.map(i => i.empresaId)).size;
    const primeiro = resumo.itens[0];
    let texto = partes.join(' e ') + (empresas > 1 ? ` em ${empresas} empresas` : '') + '.';
    if (primeiro) texto += ` Mais urgente: ${primeiro.titulo}${empresas > 1 ? ` (${primeiro.empresa})` : ''} — ${primeiro.situacaoTexto.toLowerCase()}.`;

    document.getElementById('aviso-documentos-texto').textContent = texto;
    aviso.classList.add(resumo.vencidos > 0 ? 'alert-danger' : 'alert-warning');
    aviso.classList.remove('d-none');
}

// ===================== DB1: painel financeiro =====================
// Números das notas de PRODUÇÃO autorizadas (pela competência) e dos
// contratos. "Todas as empresas" x "Esta empresa" fica lembrado neste
// navegador; o servidor decide o padrão pelo perfil da conta.
const CHAVE_ESCOPO_PAINEL = 'micoempresa.painelFinanceiro.escopo';
const MESES_CURTOS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];
const MESES_LONGOS = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro'];
let painelFinanceiro = null;

function lerEscopoPainel() {
    try { return localStorage.getItem(CHAVE_ESCOPO_PAINEL) || ''; } catch { return ''; }
}

function gravarEscopoPainel(valor) {
    try { localStorage.setItem(CHAVE_ESCOPO_PAINEL, valor); } catch { /* sem armazenamento: só não lembra */ }
}

function moedaPainel(valor, casas) {
    const digitos = casas ?? (Math.abs(valor) >= 1000 ? 0 : 2);
    return Number(valor ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: digitos, maximumFractionDigits: digitos });
}

// Eixo: "R$ 0", "R$ 500", "R$ 10 mil", "R$ 1,2 mi" (um só estilo por eixo, decidido pelo maior valor).
function moedaEixo(valor, maximo) {
    if (maximo >= 1_000_000) return `R$ ${(valor / 1_000_000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mi`;
    if (maximo >= 10_000) return `R$ ${(valor / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mil`;
    return `R$ ${valor.toLocaleString('pt-BR', { maximumFractionDigits: 0 })}`;
}

function dataDeIso(iso) {
    const [a, m, d] = iso.split('-').map(Number);
    return new Date(a, m - 1, d);
}

function nomeMes(iso, longo) {
    const data = dataDeIso(iso);
    return (longo ? MESES_LONGOS : MESES_CURTOS)[data.getMonth()];
}

function dataCurta(iso) {
    return dataDeIso(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function competenciaUrl(iso) {
    return iso.slice(0, 7);
}

function linkNotaMensal(empresaId, competenciaIso) {
    const params = new URLSearchParams();
    if (empresaId) params.set('empresaId', empresaId);
    params.set('abrirNotaMensal', '1');
    params.set('competencia', competenciaUrl(competenciaIso));
    return `/Nfse?${params}`;
}

// Escaladas "redondas" para o eixo: 1, 2, 2,5, 5 x 10^n.
function passoEixo(maximo, marcas) {
    const bruto = maximo / marcas;
    const potencia = Math.pow(10, Math.floor(Math.log10(bruto)));
    const passo = [1, 2, 2.5, 5, 10].map(f => f * potencia).find(p => p >= bruto);
    return passo ?? 10 * potencia;
}

async function carregarPainelFinanceiro() {
    const secao = document.getElementById('painel-financeiro');
    const params = new URLSearchParams();
    const empresaAtual = obterEmpresaAtualId();
    if (empresaAtual) params.set('empresaAtualId', empresaAtual);
    const escopo = lerEscopoPainel();
    if (escopo) params.set('escopo', escopo);

    let dados;
    try {
        dados = await apiFetch(`/api/painel-financeiro?${params}`);
    } catch {
        secao.classList.add('d-none'); // sem permissão ou fora do ar: o resto do painel segue
        return;
    }
    if (!dados || !dados.temDados) {
        secao.classList.add('d-none');
        return;
    }
    painelFinanceiro = dados;
    renderizarPainelFinanceiro(dados);
    secao.classList.remove('d-none');
    desenharGraficoFaturamento();
}

function renderizarPainelFinanceiro(d) {
    const temContratos = !!d.contratos;
    // Os reajustes já aparecem aqui com valor e data: o card antigo repetiria a informação.
    document.getElementById('card-contratos-reajuste')?.classList.toggle('d-none', temContratos);
    document.querySelectorAll('#painel-financeiro .pf-so-contratos').forEach(el => el.classList.toggle('d-none', !temContratos));

    // Escopo: só aparece com mais de uma empresa na conta.
    const grupoEscopo = document.getElementById('pf-escopo');
    grupoEscopo.classList.toggle('d-none', d.quantidadeEmpresas < 2 || !d.empresaNome);
    document.getElementById('pf-escopo-empresa').textContent = d.empresaNome ?? 'Esta empresa';
    grupoEscopo.querySelectorAll('button').forEach(b => {
        const ativo = (b.dataset.escopo === 'todas') === (d.escopo === 1);
        b.classList.toggle('active', ativo);
        b.setAttribute('aria-pressed', ativo ? 'true' : 'false');
        b.onclick = () => {
            if (ativo) return;
            gravarEscopoPainel(b.dataset.escopo);
            carregarPainelFinanceiro();
        };
    });
    const homologacao = d.empresasEmHomologacao > 0
        ? ` ${d.empresasEmHomologacao === 1 ? '1 empresa ainda está' : `${d.empresasEmHomologacao} empresas ainda estão`} em homologação e não entra${d.empresasEmHomologacao === 1 ? '' : 'm'} nas notas a emitir.`
        : '';
    document.getElementById('pf-subtitulo').textContent =
        (d.escopo === 1 && d.quantidadeEmpresas > 1 ? `Soma das ${d.quantidadeEmpresas} empresas. ` : '') +
        'Notas de produção autorizadas, pela competência.' + homologacao;

    // Indicadores
    const mes = nomeMes(d.mesAtual.competencia, true);
    document.getElementById('pf-rotulo-mes').textContent = `Faturado em ${mes}`;
    document.getElementById('pf-faturado-mes').textContent = moedaPainel(d.mesAtual.total);
    document.getElementById('pf-faturado-mes-detalhe').textContent =
        `${d.mesAtual.notas} ${d.mesAtual.notas === 1 ? 'nota' : 'notas'} · ${nomeMes(d.mesAnterior.competencia, true)}: ${moedaPainel(d.mesAnterior.total)}`;

    const ano = dataDeIso(d.hoje).getFullYear();
    document.getElementById('pf-rotulo-ano').textContent = `Acumulado em ${ano}`;
    document.getElementById('pf-ano').textContent = moedaPainel(d.acumuladoAno);
    document.getElementById('pf-ano-detalhe').textContent =
        `${d.notasAno} ${d.notasAno === 1 ? 'nota' : 'notas'} · 12 meses fechados: ${moedaPainel(d.ultimos12Meses)}`;

    if (temContratos) {
        const c = d.contratos;
        document.getElementById('pf-recorrente').textContent = moedaPainel(c.receitaRecorrente);
        document.getElementById('pf-recorrente-detalhe').textContent = c.contratosMensais === 0
            ? 'Nenhum contrato mensal ativo'
            : `por mês · ${c.contratosMensais} ${c.contratosMensais === 1 ? 'contrato mensal' : 'contratos mensais'}`
              + (c.reajustesImpactoEstimado > 0 ? ` · reajustes: +${moedaPainel(c.reajustesImpactoEstimado)}` : '');

        document.getElementById('pf-rotulo-a-emitir').textContent = `A emitir em ${mes}`;
        document.getElementById('pf-a-emitir').textContent = moedaPainel(c.aEmitirValor);
        document.getElementById('pf-a-emitir-detalhe').textContent = c.aEmitirQuantidade === 0
            ? 'Todos os contratos do mês já têm nota'
            : `${c.aEmitirQuantidade} ${c.aEmitirQuantidade === 1 ? 'contrato sem nota' : 'contratos sem nota'}`
              + (c.aEmitirRejeitadas > 0 ? ` · ${c.aEmitirRejeitadas} rejeitada${c.aEmitirRejeitadas === 1 ? '' : 's'}` : '');

        // Nota Mensal: a empresa dos itens, quando é uma só.
        const empresasItens = [...new Set(c.aEmitir.map(i => i.empresaId))];
        const empresaLink = d.empresaId ?? (empresasItens.length === 1 ? empresasItens[0] : null);
        const link = linkNotaMensal(empresaLink, d.mesAtual.competencia);
        document.getElementById('pf-link-a-emitir').href = link;
        const botao = document.getElementById('pf-botao-nota-mensal');
        botao.href = link;
        botao.classList.toggle('d-none', c.aEmitirQuantidade === 0);
        document.getElementById('pf-titulo-a-emitir').textContent = `Contratos sem nota em ${mes}`;
        renderizarAEmitir(c, d);
        renderizarReajustes(c);
    }

    renderizarTabelaMeses(d.meses);
    renderizarLimite(d.limite);
    renderizarMaioresClientes(d.maioresClientes, d.totalMaioresClientesJanela);
}

function renderizarAEmitir(c, d) {
    const corpo = document.querySelector('#pf-tabela-a-emitir tbody');
    if (c.aEmitir.length === 0) {
        corpo.innerHTML = `<tr><td colspan="3" class="text-center text-muted py-4"><i class="bi bi-check2-circle" aria-hidden="true"></i> Tudo emitido em ${escaparHtml(nomeMes(d.mesAtual.competencia, true))}.</td></tr>`;
        return;
    }
    const linhas = c.aEmitir.map(i => `<tr>
            <td><a href="${linkNotaMensal(i.empresaId, d.mesAtual.competencia)}">${escaparHtml(i.cliente)}</a>
                ${i.rejeitada ? ' <span class="badge text-bg-danger">Rejeitada</span>' : ''}</td>
            <td class="text-muted">${escaparHtml(i.contrato)}</td>
            <td class="text-end col-valor">${moedaPainel(i.valor, 2)}</td>
        </tr>`);
    const resto = c.aEmitirQuantidade - c.aEmitir.length;
    if (resto > 0)
        linhas.push(`<tr><td colspan="3" class="text-muted small">E mais ${resto} ${resto === 1 ? 'contrato' : 'contratos'} — veja todos na Nota Mensal.</td></tr>`);
    corpo.innerHTML = linhas.join('');
}

function renderizarReajustes(c) {
    document.getElementById('pf-titulo-reajustes').textContent = `Reajustes até ${dataCurta(c.reajustesAte).slice(0, 5)}`;
    const corpo = document.querySelector('#pf-tabela-reajustes tbody');
    const rodape = document.getElementById('pf-reajustes-rodape');
    if (c.reajustes.length === 0) {
        corpo.innerHTML = '<tr><td colspan="3" class="text-center text-muted py-4">Nenhum reajuste até o fim do mês que vem.</td></tr>';
        rodape.hidden = true;
        return;
    }
    const linhas = c.reajustes.map(r => {
        const quando = r.situacao === 'aguardando'
            ? '<span class="badge text-bg-warning">Aguardando aprovação</span>'
            : r.atrasado
                ? `<span class="badge text-bg-danger">Atrasado</span> <span class="text-muted small">${dataCurta(r.data)}</span>`
                : dataCurta(r.data);
        const valor = r.valorNovo != null
            ? `${moedaPainel(r.valorAtual, 2)} <i class="bi bi-arrow-right" aria-label="para"></i> <strong>${moedaPainel(r.valorNovo, 2)}</strong>
               <div class="small text-muted">${r.indice ? escaparHtml(r.indice) + ' ' : ''}${r.percentual != null ? (r.percentual >= 0 ? '+' : '') + Number(r.percentual).toLocaleString('pt-BR', { maximumFractionDigits: 2 }) + '%' : ''}</div>`
            : `${moedaPainel(r.valorAtual, 2)}<div class="small text-muted">${escaparHtml(r.observacao ?? 'Sem estimativa')}</div>`;
        const titulo = r.observacao && r.valorNovo != null ? ` title="${escaparHtml(r.observacao)}"` : '';
        return `<tr${titulo}>
            <td>${escaparHtml(r.cliente)}<div class="small text-muted">${escaparHtml(r.contrato)}</div></td>
            <td>${quando}</td>
            <td class="text-end">${valor}</td>
        </tr>`;
    });
    const resto = c.reajustesQuantidade - c.reajustes.length;
    if (resto > 0)
        linhas.push(`<tr><td colspan="3" class="text-muted small">E mais ${resto} — veja na tela de Contratos.</td></tr>`);
    corpo.innerHTML = linhas.join('');

    const partes = [];
    if (c.reajustesImpactoEstimado !== 0)
        partes.push(`Estimativa na receita recorrente: ${c.reajustesImpactoEstimado > 0 ? '+' : ''}${moedaPainel(c.reajustesImpactoEstimado, 2)} por mês.`);
    if (c.reajustesSemEstimativa > 0)
        partes.push(`${c.reajustesSemEstimativa} sem estimativa.`);
    rodape.textContent = partes.join(' ');
    rodape.hidden = partes.length === 0;
}

function renderizarTabelaMeses(meses) {
    document.querySelector('#pf-tabela-meses tbody').innerHTML = meses.slice().reverse().map(m => `<tr>
            <td>${nomeMes(m.competencia)}/${m.competencia.slice(0, 4)}</td>
            <td class="text-end">${moedaPainel(m.contratos, 2)}</td>
            <td class="text-end">${moedaPainel(m.avulsas, 2)}</td>
            <td class="text-end"><strong>${moedaPainel(m.total, 2)}</strong></td>
            <td class="text-end">${m.notas}</td>
        </tr>`).join('');
}

function renderizarLimite(limite) {
    const caixa = document.getElementById('pf-limite');
    document.getElementById('pf-titulo-clientes').innerHTML = limite
        ? 'Regime e clientes'
        : 'Maiores clientes <small class="text-muted">(12 meses)</small>';
    document.getElementById('pf-subtitulo-clientes').classList.toggle('d-none', !limite);
    if (!limite) {
        caixa.classList.add('d-none');
        return;
    }
    const percentual = Math.min(100, Number(limite.percentual ?? 0));
    const icone = limite.nivel === 'alerta' ? 'bi-exclamation-octagon' : limite.nivel === 'atencao' ? 'bi-exclamation-triangle' : 'bi-speedometer2';
    caixa.className = `pf-limite pf-limite-${limite.nivel}`;
    caixa.innerHTML = `
        <div class="d-flex justify-content-between align-items-baseline gap-2">
            <strong><i class="bi ${icone}" aria-hidden="true"></i> ${escaparHtml(limite.titulo)}</strong>
            <span class="pf-limite-valor">${moedaPainel(limite.base)}${limite.limite ? ` <span class="text-muted">de ${moedaPainel(limite.limite)}</span>` : ''}</span>
        </div>
        <div class="progress" role="progressbar" aria-label="${escaparHtml(limite.titulo)}" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${Math.round(percentual)}">
            <div class="progress-bar"></div>
        </div>
        <p>${escaparHtml(limite.texto)}</p>`;
    caixa.querySelector('.progress-bar').style.width = `${percentual}%`;
}

function renderizarMaioresClientes(clientes, total) {
    const lista = document.getElementById('pf-clientes');
    if (!clientes.length) {
        lista.innerHTML = '<li class="text-muted">Nenhuma nota de produção nos últimos 12 meses.</li>';
        return;
    }
    lista.innerHTML = clientes.map(c => `<li>
            <div class="d-flex justify-content-between gap-2">
                <span class="text-truncate">${escaparHtml(c.nome)}</span>
                <span class="pf-clientes-valor">${moedaPainel(c.valor)} <span class="text-muted">${Number(c.percentual).toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%</span></span>
            </div>
            <div class="pf-fatia"><span></span></div>
        </li>`).join('');
    lista.querySelectorAll('.pf-fatia > span').forEach((barra, i) => { barra.style.width = `${Math.max(2, clientes[i].percentual)}%`; });
    if (clientes[0].percentual >= 50)
        lista.insertAdjacentHTML('beforeend', `<li class="pf-concentracao"><i class="bi bi-info-circle" aria-hidden="true"></i> ${escaparHtml(clientes[0].nome)} responde por metade ou mais do faturamento: perder esse cliente pesa muito.</li>`);
}

// Colunas empilhadas: contratos (embaixo) e avulsas (em cima), 2px de
// respiro entre as partes, ponta arredondada só no topo da coluna. Sem
// número nas colunas: o mês atual está no cartão de cima e cada mês na dica
// (mouse, toque ou teclado) e em "Ver em tabela".
function desenharGraficoFaturamento() {
    const d = painelFinanceiro;
    const caixa = document.getElementById('pf-grafico');
    if (!d || !caixa || caixa.offsetParent === null) return;

    const meses = d.meses;
    const largura = Math.max(280, caixa.clientWidth);
    const altura = 220;
    const margem = { topo: 18, direita: 4, baixo: 26, esquerda: 64 };
    const areaL = largura - margem.esquerda - margem.direita;
    const areaA = altura - margem.topo - margem.baixo;
    const maiorTotal = Math.max(...meses.map(m => m.total), 0);
    const passo = maiorTotal > 0 ? passoEixo(maiorTotal, 4) : 1;
    const teto = maiorTotal > 0 ? Math.ceil(maiorTotal / passo) * passo : 4;
    const y = v => margem.topo + areaA - (v / teto) * areaA;
    const banda = areaL / meses.length;
    const larguraBarra = Math.min(24, banda * 0.6);
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', `0 0 ${largura} ${altura}`);
    svg.setAttribute('width', '100%');
    svg.setAttribute('height', String(altura));
    svg.setAttribute('aria-hidden', 'true');

    const el = (tag, atributos, texto) => {
        const e = document.createElementNS(ns, tag);
        Object.entries(atributos).forEach(([k, v]) => e.setAttribute(k, String(v)));
        if (texto != null) e.textContent = texto;
        return e;
    };

    for (let v = 0; v <= teto + passo / 2; v += passo) {
        svg.appendChild(el('line', { x1: margem.esquerda, x2: largura - margem.direita, y1: y(v), y2: y(v), class: v === 0 ? 'pf-eixo' : 'pf-grade' }));
        svg.appendChild(el('text', { x: margem.esquerda - 8, y: y(v) + 4, 'text-anchor': 'end', class: 'pf-texto-eixo' }, moedaEixo(v, teto)));
    }

    // Coluna com a ponta de cima arredondada (4px) e a base reta.
    const caminhoColuna = (x, topo, base, raio) => {
        const r = Math.min(raio, (base - topo), larguraBarra / 2);
        return `M${x},${base} V${topo + r} Q${x},${topo} ${x + r},${topo} H${x + larguraBarra - r} Q${x + larguraBarra},${topo} ${x + larguraBarra},${topo + r} V${base} Z`;
    };

    const dica = document.getElementById('pf-dica');
    const ultimo = meses.length - 1;
    meses.forEach((m, i) => {
        const x = margem.esquerda + banda * i + (banda - larguraBarra) / 2;
        const base = y(0);
        const topoContratos = y(m.contratos);
        const temAvulsas = m.avulsas > 0;
        if (m.contratos > 0) {
            svg.appendChild(el('path', { d: caminhoColuna(x, topoContratos, base, temAvulsas ? 0 : 4), class: 'pf-barra-contratos' }));
        }
        if (temAvulsas) {
            const baseAvulsas = m.contratos > 0 ? topoContratos - 2 : base;
            const topoAvulsas = Math.min(y(m.total), baseAvulsas - 1);
            svg.appendChild(el('path', { d: caminhoColuna(x, topoAvulsas, baseAvulsas, 4), class: 'pf-barra-avulsas' }));
        }

        // Tela estreita: um mês sim, outro não (sempre com o atual), para os nomes não se encostarem.
        const comAno = banda >= 44 && (m.competencia.slice(5, 7) === '01' || i === 0);
        if (banda >= 30 || (ultimo - i) % 2 === 0) {
            const rotulo = nomeMes(m.competencia) + (comAno ? `/${m.competencia.slice(2, 4)}` : '');
            svg.appendChild(el('text', { x: margem.esquerda + banda * i + banda / 2, y: altura - 8, 'text-anchor': 'middle', class: 'pf-texto-eixo' + (i === ultimo ? ' pf-texto-atual' : '') }, rotulo));
        }

        // Alvo da dica: a banda inteira (maior que a coluna), também pelo teclado.
        const alvo = el('rect', { x: margem.esquerda + banda * i, y: margem.topo, width: banda, height: areaA, class: 'pf-alvo', tabindex: 0 });
        const texto = `${nomeMes(m.competencia, true)} de ${m.competencia.slice(0, 4)}${i === ultimo ? ' (em andamento)' : ''}`;
        alvo.setAttribute('aria-label', `${texto}: ${moedaPainel(m.total, 2)}`);
        const mostrar = () => {
            dica.innerHTML = `<strong>${escaparHtml(texto)}</strong>
                <div><i class="pf-amostra pf-amostra-contratos"></i>Contratos <span>${moedaPainel(m.contratos, 2)}</span></div>
                <div><i class="pf-amostra pf-amostra-avulsas"></i>Avulsas <span>${moedaPainel(m.avulsas, 2)}</span></div>
                <div class="pf-dica-total">Total <span>${moedaPainel(m.total, 2)}</span></div>
                <div class="text-muted">${m.notas} ${m.notas === 1 ? 'nota' : 'notas'}</div>`;
            dica.hidden = false;
            const centro = margem.esquerda + banda * i + banda / 2;
            const larguraDica = dica.offsetWidth;
            dica.style.left = `${Math.min(Math.max(0, centro - larguraDica / 2), largura - larguraDica)}px`;
            dica.style.top = `${Math.max(0, y(m.total) - dica.offsetHeight - 10)}px`;
            alvo.classList.add('pf-alvo-ativo');
        };
        const esconder = () => { dica.hidden = true; alvo.classList.remove('pf-alvo-ativo'); };
        alvo.addEventListener('mouseenter', mostrar);
        alvo.addEventListener('focus', mostrar);
        alvo.addEventListener('mouseleave', esconder);
        alvo.addEventListener('blur', esconder);
        alvo.addEventListener('click', mostrar);
        svg.appendChild(alvo);
    });

    caixa.replaceChildren(svg);
    const ultimoMes = meses[ultimo];
    caixa.setAttribute('aria-label',
        `Faturamento dos últimos 12 meses. ${nomeMes(ultimoMes.competencia, true)}: ${moedaPainel(ultimoMes.total, 2)}. Maior mês: ${moedaPainel(maiorTotal, 2)}. Use "Ver em tabela" para todos os valores.`);
}

document.getElementById('pf-alternar-tabela')?.addEventListener('click', function () {
    const tabela = document.getElementById('pf-tabela-meses');
    const grafico = document.getElementById('pf-grafico');
    const mostrandoTabela = tabela.classList.toggle('d-none') === false;
    grafico.classList.toggle('d-none', mostrandoTabela);
    document.getElementById('pf-dica').hidden = true;
    this.textContent = mostrandoTabela ? 'Ver gráfico' : 'Ver em tabela';
    this.setAttribute('aria-expanded', mostrandoTabela ? 'true' : 'false');
    if (!mostrandoTabela) desenharGraficoFaturamento();
});

let temporizadorGrafico;
window.addEventListener('resize', () => {
    clearTimeout(temporizadorGrafico);
    temporizadorGrafico = setTimeout(desenharGraficoFaturamento, 150);
});

carregarPainelFinanceiro();
carregarDashboard();
carregarOnboarding();
carregarLembreteWhatsApp();
carregarAvisoDocumentos();
