// Tela "Meus dados": pedir a exportação dos dados da conta e baixar o ZIP.
document.addEventListener('DOMContentLoaded', function () {
    const botao = document.getElementById('btn-exportar');
    const aviso = document.getElementById('aviso-exportacao');
    const lista = document.getElementById('lista-exportacoes');
    let temporizador = null;

    // StatusExportacaoDados (Domain.Enums): 1 Pendente, 2 Gerando, 3 Pronta, 4 Erro, 5 Expirada.
    function situacao(e) {
        switch (e.status) {
            case 1: return '<span class="badge text-bg-secondary">Na fila</span>';
            case 2: return '<span class="badge text-bg-info"><span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Montando</span>';
            case 3: return e.podeBaixar
                ? '<span class="badge text-bg-success">Pronta</span>'
                : '<span class="badge text-bg-secondary">Expirada</span>';
            case 4: return '<span class="badge text-bg-danger">Falhou</span>';
            default: return '<span class="badge text-bg-secondary">Expirada</span>';
        }
    }

    function tamanho(bytes) {
        if (bytes == null) return '';
        if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
        return `${(bytes / (1024 * 1024)).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} MB`;
    }

    function dataHora(iso) {
        return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
    }

    function mostrar(dados) {
        document.getElementById('dias-validade').textContent = dados.diasValidade;

        botao.disabled = !!dados.motivo;
        aviso.classList.toggle('d-none', !dados.motivo);
        aviso.textContent = dados.motivo || '';

        if (!dados.podeExportar) {
            lista.innerHTML = '<li class="text-muted">Só o administrador da conta vê e baixa as exportações.</li>';
            return;
        }
        if (dados.exportacoes.length === 0) {
            lista.innerHTML = '<li class="text-muted">Nenhuma exportação ainda.</li>';
            return;
        }

        lista.innerHTML = dados.exportacoes.map(function (e) {
            const detalhes = [];
            if (e.quantidadeNotas != null) detalhes.push(`${e.quantidadeNotas} nota(s)`);
            if (e.tamanhoBytes != null) detalhes.push(tamanho(e.tamanhoBytes));
            if (e.podeBaixar && e.expiraEm) detalhes.push(`disponível até ${new Date(e.expiraEm).toLocaleDateString('pt-BR')}`);
            const baixar = e.podeBaixar
                ? `<a class="btn btn-sm btn-success" href="/api/dados-conta/exportacoes/${encodeURIComponent(e.id)}/arquivo"><i class="bi bi-download" aria-hidden="true"></i> Baixar</a>`
                : '';
            const erro = e.status === 4 && e.erro ? `<div class="small text-danger">${escaparHtml(e.erro)}</div>` : '';
            return `<li class="py-2 border-bottom">
                <div class="d-flex justify-content-between align-items-center gap-2">
                    <span>Pedida em ${dataHora(e.pedidaEm)}</span>${situacao(e)}
                </div>
                <div class="d-flex justify-content-between align-items-center gap-2 mt-1">
                    <span class="small text-muted">${escaparHtml(detalhes.join(' · '))}</span>${baixar}
                </div>${erro}</li>`;
        }).join('');

        // Enquanto houver pedido em andamento, confere de novo a cada 10 segundos.
        clearTimeout(temporizador);
        if (dados.exportacoes.some(e => e.status === 1 || e.status === 2))
            temporizador = setTimeout(carregar, 10000);
    }

    async function carregar() {
        try {
            mostrar(await apiFetch('/api/dados-conta/exportacoes'));
        } catch (err) {
            lista.innerHTML = '<li class="text-danger">Não foi possível carregar.</li>';
            mostrarErro(err.message);
        }
    }

    botao.addEventListener('click', async function () {
        await executarComBotaoDesabilitado(botao, async function () {
            try {
                await apiFetch('/api/dados-conta/exportacoes', { method: 'POST' });
                mostrarToast('Exportação pedida. Avisamos por e-mail quando estiver pronta.');
            } catch (err) {
                mostrarErro(err.message);
            }
        });
        await carregar();
    });

    document.getElementById('btn-atualizar').addEventListener('click', carregar);
    carregar();
});
