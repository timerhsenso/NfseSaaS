// Tela "Assinatura": situação, planos, assinar, pagar fatura e cancelar.
// StatusAssinatura (Domain.Enums): 1 Teste, 2 Ativa, 3 Atrasada, 4 Cancelada.
const SITUACOES_ASSINATURA = {
    1: { texto: 'Teste grátis', cor: 'info' },
    2: { texto: 'Ativa', cor: 'success' },
    3: { texto: 'Fatura em atraso', cor: 'danger' },
    4: { texto: 'Cancelada', cor: 'secondary' }
};

function reais(valor) {
    return Number(valor).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

function dataBr(iso) {
    return iso ? new Date(iso).toLocaleDateString('pt-BR') : '—';
}

// Data sem hora (DateOnly, "2026-12-10"): formata sem passar por Date, que
// leria como meia-noite UTC e mostraria o dia anterior no horário de Brasília.
function diaBr(iso) {
    if (!iso) return '—';
    const [ano, mes, dia] = iso.split('-');
    return `${dia}/${mes}/${ano}`;
}

function hojeIso() {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

// Última leitura da tela (para os textos de confirmação).
let assinaturaAtual = null;

function textoFundador(a) {
    if (a.fundador && a.status !== 4) return 'sim, preso enquanto a assinatura estiver ativa';
    if (a.fundadorReservado) {
        if (a.fundadorReservaSemPrazo) return 'garantido para você';
        return a.fundadorReservaAte
            ? `garantido para você até ${diaBr(a.fundadorReservaAte)}`
            : 'garantido para você até 7 dias depois do fim do teste';
    }
    if (a.vagasFundadorRestantes > 0)
        return `${a.vagasFundadorRestantes} vaga(s)${a.campanhaFundadorAte ? ` até ${diaBr(a.campanhaFundadorAte)}` : ''}`;
    return 'vagas encerradas';
}

async function carregarAssinatura() {
    let a;
    try {
        a = await apiFetch('/api/assinatura');
    } catch (err) {
        mostrarErro(err.message);
        return;
    }

    assinaturaAtual = a;
    const alerta = document.getElementById('assinatura-alerta');
    const primeiraNoFuturo = a.primeiraCobrancaEm && a.primeiraCobrancaEm > hojeIso();
    const mensagem = !a.cobrancaAtiva
        ? { cor: 'info', texto: 'Estamos em beta: o uso é gratuito. Quando a cobrança começar, você será avisado com antecedência.'
            + (a.fundadorReservado ? ' Seu preço de fundador já está garantido.' : ' Quem entrar agora garante o preço de fundador.') }
        : a.isenta ? { cor: 'info', texto: 'Esta conta é isenta de cobrança.' }
        : a.bloqueio ? { cor: 'danger', texto: a.bloqueio }
        : a.aguardandoPrimeiroPagamento && primeiraNoFuturo
            ? { cor: 'info', texto: `Assinatura criada. Você segue no teste grátis, e a primeira fatura vence em ${diaBr(a.primeiraCobrancaEm)}: pode pagar antes ou até lá pelo botão "Pagar fatura".` }
        : a.aguardandoPrimeiroPagamento ? { cor: 'warning', texto: 'Assinatura criada: falta pagar a primeira fatura. A confirmação é automática (Pix em segundos, boleto em até 3 dias úteis).' }
        : a.aviso ? { cor: a.status === 4 ? 'info' : 'warning', texto: a.aviso }
        : null;
    alerta.className = mensagem ? `alert alert-${mensagem.cor}` : 'alert d-none';
    alerta.textContent = mensagem?.texto ?? '';

    const situacao = SITUACOES_ASSINATURA[a.status] ?? { texto: '—', cor: 'secondary' };
    document.getElementById('ass-situacao').innerHTML = `<span class="badge text-bg-${situacao.cor}">${situacao.texto}</span>`;
    document.getElementById('ass-plano').textContent = a.planoNome
        ? `${a.planoNome} · ${reais(a.precoMensal)}/mês${a.fundador ? ' (fundador)' : ''}`
            + (a.reajusteAplicarEm ? ` · a partir de ${diaBr(a.reajusteAplicarEm)}: ${reais(a.reajustePrecoNovo)}/mês (reajuste avisado por e-mail)` : '')
        : '—';
    document.getElementById('ass-teste').textContent = a.semTesteGratis
        ? 'não disponível: o CNPJ já usou o teste grátis em outra conta'
        : a.testeIniciadoEm
            ? `30 dias, sem limite de notas · até ${dataBr(a.testeTerminaEm)}`
            : '30 dias, sem limite de notas · começa na primeira nota em produção';
    document.getElementById('ass-fundador').textContent = textoFundador(a);
    const temPrimeira = !!a.primeiraCobrancaEm;
    document.getElementById('ass-primeira-cobranca-rotulo').classList.toggle('d-none', !temPrimeira);
    const primeira = document.getElementById('ass-primeira-cobranca');
    primeira.classList.toggle('d-none', !temPrimeira);
    primeira.textContent = temPrimeira
        ? (a.podeAssinar ? `${diaBr(a.primeiraCobrancaEm)} (se assinar hoje)` : diaBr(a.primeiraCobrancaEm))
        : '';
    document.getElementById('ass-notas-mes').textContent = a.notasProducaoNoMes;
    document.getElementById('ass-empresas').textContent = a.empresasAtivas;

    const pagar = document.getElementById('btn-pagar-fatura');
    pagar.classList.toggle('d-none', !a.urlFaturaPendente);
    if (a.urlFaturaPendente) pagar.href = a.urlFaturaPendente;
    document.getElementById('btn-cancelar-assinatura').classList.toggle('d-none', !a.podeCancelar);

    document.getElementById('ass-vagas').textContent = a.fundadorReservado
        ? 'preço de fundador garantido para você'
        : a.vagasFundadorRestantes > 0
            ? `${a.vagasFundadorRestantes} vaga(s) com preço de fundador${a.campanhaFundadorAte ? ` até ${diaBr(a.campanhaFundadorAte)}` : ''}`
            : '';

    document.getElementById('ass-planos').innerHTML = a.planos.map(p => {
        const atual = a.plano === p.codigo && a.status !== 4;
        const preco = p.precoDeFundador
            ? `<span class="text-muted text-decoration-line-through me-1">${reais(p.precoNormal)}</span><strong class="fs-4">${reais(p.preco)}</strong><small>/mês</small><div class="small text-success fw-semibold">preço de fundador${a.fundadorReservado ? ' · garantido para você' : ` · ${p.vagasFundadorRestantes} vaga(s)`}</div>`
            : `<strong class="fs-4">${reais(p.preco)}</strong><small>/mês</small>`;
        const excede = a.empresasAtivas > p.maxEmpresas;
        const botao = atual
            ? '<span class="badge text-bg-success">Seu plano</span>'
            : a.podeAssinar
                ? `<button type="button" class="btn btn-primary w-100 btn-assinar" data-plano="${p.codigo}" data-nome="${escaparHtml(p.nome)}" data-preco="${p.preco}">Assinar ${escaparHtml(p.nome)}</button>`
                : a.podeTrocarPlano
                    ? excede
                        ? `<div class="small text-muted">Para trocar, a conta precisa ter até ${p.maxEmpresas} empresa(s) ativa(s) (hoje: ${a.empresasAtivas}).</div>`
                        : `<button type="button" class="btn btn-outline-primary w-100 btn-trocar-plano" data-plano="${p.codigo}" data-nome="${escaparHtml(p.nome)}" data-preco="${p.preco}">Trocar para ${escaparHtml(p.nome)}</button>`
                    : '';
        return `<div class="col-md-6"><div class="border rounded-3 p-3 h-100 d-flex flex-column">
                    <h3 class="h6 mb-0">${escaparHtml(p.nome)}</h3>
                    <p class="small text-muted mb-2">${escaparHtml(p.descricao)}</p>
                    <div class="mb-2">${preco}</div>
                    <ul class="small mb-3 ps-3 flex-grow-1">
                        <li>até ${p.maxEmpresas} empresa(s)</li>
                        <li>${p.notasPorMes ? `até ${p.notasPorMes} notas por mês` : 'notas sem limite'}</li>
                        <li>nota mensal automática, reajuste e e-mail ao cliente</li>
                    </ul>
                    ${botao}
                </div></div>`;
    }).join('');
}

document.addEventListener('DOMContentLoaded', function () {
    document.getElementById('ass-planos').addEventListener('click', async function (e) {
        const botao = e.target.closest('.btn-assinar');
        if (!botao) return;
        await executarComBotaoDesabilitado(botao, async function () {
            const venc = assinaturaAtual?.primeiraCobrancaEm;
            const venceHoje = !venc || venc <= hojeIso();
            const quando = venceHoje
                ? 'A primeira fatura vence hoje e abre numa nova aba para pagamento.'
                : `A primeira fatura só vence em ${diaBr(venc)}, quando o teste grátis acaba. Até lá você continua no teste.`;
            if (!await confirmarAcao(
                `Assinar o plano ${botao.dataset.nome} por ${reais(botao.dataset.preco)} por mês? ${quando}`,
                { titulo: 'Assinar', textoBotao: venceHoje ? 'Assinar e pagar' : 'Assinar' })) return;
            try {
                const r = await apiFetch('/api/assinatura/assinar', { method: 'POST', body: JSON.stringify({ plano: Number(botao.dataset.plano) }) });
                if (venceHoje && r.urlFatura) window.open(r.urlFatura, '_blank', 'noopener');
                mostrarToast(venceHoje
                    ? 'Assinatura criada. Pague a fatura para ativar.'
                    : `Assinatura criada. A primeira fatura vence em ${diaBr(venc)}.`);
                await carregarAssinatura();
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });

    document.getElementById('ass-planos').addEventListener('click', async function (e) {
        const botao = e.target.closest('.btn-trocar-plano');
        if (!botao) return;
        await executarComBotaoDesabilitado(botao, async function () {
            if (!await confirmarAcao(
                `Trocar para o plano ${botao.dataset.nome} por ${reais(botao.dataset.preco)} por mês? O novo valor vale a partir da próxima fatura (inclusive a que já estiver em aberto).`,
                { titulo: 'Trocar de plano', textoBotao: 'Trocar plano' })) return;
            try {
                const r = await apiFetch('/api/assinatura/trocar-plano', { method: 'POST', body: JSON.stringify({ plano: Number(botao.dataset.plano) }) });
                mostrarToast(`Plano trocado: ${r.descricao}.`);
                await carregarAssinatura();
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });

    document.getElementById('btn-cancelar-assinatura').addEventListener('click', async function () {
        await executarComBotaoDesabilitado(this, async function () {
            const fundador = assinaturaAtual?.fundador
                ? ' Você perde o preço de fundador: se voltar, paga o preço do dia.'
                : '';
            if (!await confirmarAcao(
                `Cancelar a assinatura? Não haverá novas faturas. A emissão em produção continua até o fim do mês já pago (ou do teste grátis) e depois para.${fundador} As notas emitidas continuam valendo e você pode baixá-las.`,
                { titulo: 'Cancelar assinatura', textoBotao: 'Cancelar assinatura', variante: 'perigo' })) return;
            try {
                await apiFetch('/api/assinatura/cancelar', { method: 'POST' });
                mostrarToast('Assinatura cancelada.');
                await carregarAssinatura();
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });

    carregarAssinatura();
});
