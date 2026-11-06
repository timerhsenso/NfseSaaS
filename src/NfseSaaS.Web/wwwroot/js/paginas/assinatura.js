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

async function carregarAssinatura() {
    let a;
    try {
        a = await apiFetch('/api/assinatura');
    } catch (err) {
        mostrarErro(err.message);
        return;
    }

    const alerta = document.getElementById('assinatura-alerta');
    const mensagem = !a.cobrancaAtiva
        ? { cor: 'info', texto: 'Estamos em beta: o uso é gratuito. Quando a cobrança começar, você será avisado com antecedência e quem entrar agora garante o preço de fundador.' }
        : a.isenta ? { cor: 'info', texto: 'Esta conta é isenta de cobrança.' }
        : a.bloqueio ? { cor: 'danger', texto: a.bloqueio }
        : a.aguardandoPrimeiroPagamento ? { cor: 'warning', texto: 'Assinatura criada: falta pagar a primeira fatura. A confirmação é automática (Pix em segundos, boleto em até 3 dias úteis).' }
        : a.aviso ? { cor: 'warning', texto: a.aviso }
        : null;
    alerta.className = mensagem ? `alert alert-${mensagem.cor}` : 'alert d-none';
    alerta.textContent = mensagem?.texto ?? '';

    const situacao = SITUACOES_ASSINATURA[a.status] ?? { texto: '—', cor: 'secondary' };
    document.getElementById('ass-situacao').innerHTML = `<span class="badge text-bg-${situacao.cor}">${situacao.texto}</span>`;
    document.getElementById('ass-plano').textContent = a.planoNome
        ? `${a.planoNome} · ${reais(a.precoMensal)}/mês${a.fundador ? ' (fundador)' : ''}`
        : '—';
    document.getElementById('ass-teste').textContent = a.testeIniciadoEm
        ? `${a.notasTesteUsadas} de ${a.notasTesteLimite} notas · até ${dataBr(a.testeTerminaEm)}`
        : 'começa na primeira nota em produção';
    document.getElementById('ass-notas-mes').textContent = a.notasProducaoNoMes;
    document.getElementById('ass-empresas').textContent = a.empresasAtivas;

    const pagar = document.getElementById('btn-pagar-fatura');
    pagar.classList.toggle('d-none', !a.urlFaturaPendente);
    if (a.urlFaturaPendente) pagar.href = a.urlFaturaPendente;
    document.getElementById('btn-cancelar-assinatura').classList.toggle('d-none', !a.podeCancelar);

    document.getElementById('ass-vagas').textContent = a.vagasFundadorRestantes > 0
        ? `${a.vagasFundadorRestantes} vaga(s) com preço de fundador`
        : '';

    document.getElementById('ass-planos').innerHTML = a.planos.map(p => {
        const atual = a.plano === p.codigo && a.status !== 4;
        const preco = p.precoDeFundador
            ? `<span class="text-muted text-decoration-line-through me-1">${reais(p.precoNormal)}</span><strong class="fs-4">${reais(p.preco)}</strong><small>/mês</small><div class="small text-success fw-semibold">preço de fundador · ${p.vagasFundadorRestantes} vaga(s)</div>`
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
            if (!await confirmarAcao(
                `Assinar o plano ${botao.dataset.nome} por ${reais(botao.dataset.preco)} por mês? A primeira fatura vence hoje e abre numa nova aba para pagamento.`,
                { titulo: 'Assinar', textoBotao: 'Assinar e pagar' })) return;
            try {
                const r = await apiFetch('/api/assinatura/assinar', { method: 'POST', body: JSON.stringify({ plano: Number(botao.dataset.plano) }) });
                if (r.urlFatura) window.open(r.urlFatura, '_blank', 'noopener');
                mostrarToast('Assinatura criada. Pague a fatura para ativar.');
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
            if (!await confirmarAcao(
                'Cancelar a assinatura? Não haverá novas faturas e a emissão em produção será bloqueada. As notas emitidas continuam valendo e você pode baixá-las.',
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
