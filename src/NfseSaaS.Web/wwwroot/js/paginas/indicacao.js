// Tela "Indique e ganhe": link da conta, atalhos de envio, convite por e-mail e as contas indicadas.
document.addEventListener('DOMContentLoaded', async function () {
    const campo = document.getElementById('link-indicacao');
    const lista = document.getElementById('lista-indicacoes');

    let dados;
    try {
        dados = await apiFetch('/api/indicacao');
    } catch (err) {
        campo.value = '';
        lista.innerHTML = '<li class="text-danger">Não foi possível carregar.</li>';
        mostrarErro(err.message);
        return;
    }

    campo.value = dados.link;
    const mensagem = `Uso o micoempresa para emitir as notas fiscais de serviço: a nota mensal sai sozinha e o cliente recebe por e-mail. Crie sua conta grátis: ${dados.link}`;
    document.getElementById('btn-whatsapp').href = `https://wa.me/?text=${encodeURIComponent(mensagem)}`;
    document.getElementById('btn-email').href =
        `mailto:?subject=${encodeURIComponent('Emissor de nota fiscal de serviço')}&body=${encodeURIComponent(mensagem)}`;

    document.getElementById('btn-copiar-link').addEventListener('click', async function () {
        try {
            await navigator.clipboard.writeText(dados.link);
            mostrarToast('Link copiado.');
        } catch {
            campo.select();
            mostrarToast('Selecione e copie o link com Ctrl+C.');
        }
    });

    // ---------- Convite por e-mail ----------
    const listaConvites = document.getElementById('lista-convites');
    const resultadoConvites = document.getElementById('resultado-convites');

    function mostrarConvites(convites, restam) {
        document.getElementById('restam-convites').textContent = restam;
        if (!convites || convites.length === 0) {
            listaConvites.innerHTML = '<li class="text-muted">Nenhum convite enviado.</li>';
            return;
        }
        listaConvites.innerHTML = convites.map(c =>
            `<li class="d-flex justify-content-between py-1 border-bottom"><span>${escaparHtml(c.email)}</span>` +
            `<span class="text-muted">${new Date(c.enviadoEm).toLocaleDateString('pt-BR')}</span></li>`).join('');
    }

    document.getElementById('limite-convites').textContent = dados.limitePorDia;
    mostrarConvites(dados.convites, dados.convitesRestamHoje);

    document.getElementById('form-convite').addEventListener('submit', async function (e) {
        e.preventDefault();
        const campoEmails = document.getElementById('emails-convite');
        if (!campoEmails.value.trim()) {
            mostrarErro('Informe pelo menos um e-mail.');
            return;
        }
        await executarComBotaoDesabilitado(document.getElementById('btn-enviar-convites'), async function () {
            try {
                const r = await apiFetch('/api/indicacao/convites', { method: 'POST', body: JSON.stringify({ emails: campoEmails.value }) });
                resultadoConvites.classList.remove('d-none');
                resultadoConvites.innerHTML = r.resultados.map(x => x.enviado
                    ? `<li class="text-success"><i class="bi bi-check-circle" aria-hidden="true"></i> ${escaparHtml(x.email)}: convite enviado.</li>`
                    : `<li class="text-danger"><i class="bi bi-x-circle" aria-hidden="true"></i> ${escaparHtml(x.email)}: ${escaparHtml(x.motivo)}</li>`).join('');
                const enviados = r.resultados.filter(x => x.enviado).length;
                if (enviados > 0) {
                    mostrarToast(enviados === 1 ? 'Convite enviado.' : `${enviados} convites enviados.`);
                    campoEmails.value = '';
                    const atual = await apiFetch('/api/indicacao');
                    mostrarConvites(atual.convites, atual.convitesRestamHoje);
                } else {
                    document.getElementById('restam-convites').textContent = r.restamHoje;
                }
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });

    document.getElementById('total-indicacoes').textContent = dados.indicadas.length;
    if (dados.indicadas.length === 0) {
        lista.innerHTML = '<li class="text-muted">Ninguém se cadastrou pelo seu link ainda.</li>';
        return;
    }

    // StatusPremioIndicacao (Domain.Enums): 1 Pendente, 2 Aplicando, 3 Aplicado, 4 Erro.
    function premio(i) {
        switch (i.premio) {
            case 3: return `<span class="badge text-bg-success" title="Concedido em ${new Date(i.premioAplicadoEm).toLocaleDateString('pt-BR')}"><i class="bi bi-gift" aria-hidden="true"></i> 1 mês grátis ganho</span>`;
            case 1: return '<span class="badge text-bg-info" title="Vale assim que a sua assinatura estiver em dia"><i class="bi bi-gift" aria-hidden="true"></i> 1 mês grátis guardado</span>';
            case 2:
            case 4: return '<span class="badge text-bg-info"><i class="bi bi-gift" aria-hidden="true"></i> Prêmio em processamento</span>';
            default: return null;
        }
    }

    lista.innerHTML = dados.indicadas.map(function (i) {
        const data = new Date(i.cadastradaEm).toLocaleDateString('pt-BR');
        const situacao = premio(i) ?? (!i.ativa
            ? '<span class="badge text-bg-secondary">Conta suspensa</span>'
            : i.jaEmitiuNota
                ? '<span class="badge text-bg-success">Já emite notas</span>'
                : '<span class="badge text-bg-warning">Configurando</span>');
        return `<li class="d-flex justify-content-between align-items-center py-2 border-bottom"><span>Conta criada em ${data}</span>${situacao}</li>`;
    }).join('');
});
