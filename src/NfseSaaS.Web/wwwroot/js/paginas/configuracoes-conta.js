// Configurações da conta › Identidade visual: logo (B9) e cores da conta.
// Só o administrador muda (o servidor confere de novo); os demais veem a
// tela só para leitura. Logo vale na hora; cores: clique pré-visualiza,
// "Salvar cores" grava (sair sem salvar volta ao que estava).
document.addEventListener('DOMContentLoaded', async function () {
    const raiz = document.documentElement;
    const previa = document.getElementById('logo-previa');
    const botaoRemover = document.getElementById('btn-remover-logo');
    const arquivoLogo = document.getElementById('arquivo-logo');
    const botaoSalvarCores = document.getElementById('btn-salvar-cores');
    const botaoDesfazerCores = document.getElementById('btn-desfazer-cores');
    const paletas = document.querySelectorAll('.config-paletas .aparencia-paleta');

    let paletaSalva = raiz.dataset.paleta || 'mico';
    let paletaEscolhida = paletaSalva;

    function urlLogo(versao) {
        return versao ? `/api/aparencia/logo?v=${versao}` : null;
    }

    function mostrarPrevia(versao) {
        previa.innerHTML = versao
            ? `<img src="${urlLogo(versao)}" alt="Logo da conta" />`
            : '<span class="text-muted small">Sem logo: usando a marca do micoempresa.</span>';
        botaoRemover.classList.toggle('d-none', !versao);
    }

    // Depois de enviar/remover: prévia, impressão e menu. Trocar um logo por
    // outro troca a imagem do menu na hora; passar de "sem logo" para "com
    // logo" (ou o contrário) muda o topo do menu, que vem pronto do servidor
    // — recarrega a página.
    function aplicarLogo(versao) {
        mostrarPrevia(versao);
        const url = urlLogo(versao);
        if (url) document.body.dataset.logoConta = url; else delete document.body.dataset.logoConta;

        const noMenu = document.getElementById('logo-conta-menu');
        if (noMenu && url) noMenu.src = url;
        if (!!noMenu !== !!url) setTimeout(() => location.reload(), 700);
    }

    function marcarPaleta() {
        paletas.forEach(b => {
            const sel = b.dataset.paleta === paletaEscolhida;
            b.classList.toggle('selecionada', sel);
            b.setAttribute('aria-checked', sel ? 'true' : 'false');
        });
        const mudou = paletaEscolhida !== paletaSalva;
        botaoSalvarCores.disabled = !mudou;
        botaoDesfazerCores.classList.toggle('d-none', !mudou);
    }

    function escolherPaleta(codigo) {
        paletaEscolhida = codigo;
        raiz.dataset.paleta = codigo; // pré-visualização na tela toda
        marcarPaleta();
    }

    let dados;
    try {
        dados = await apiFetch('/api/aparencia');
    } catch (err) {
        mostrarErro(err.message);
        return;
    }

    paletaSalva = dados.paleta;
    paletaEscolhida = dados.paleta;
    mostrarPrevia(dados.logoVersao);
    marcarPaleta();

    if (!dados.podeAlterarPaleta) {
        // Usuário comum: só vê.
        document.getElementById('config-conta-intro').textContent =
            'Vale para todos os usuários desta conta. Só o administrador da conta pode mudar.';
        document.getElementById('logo-acoes').classList.add('d-none');
        document.getElementById('logo-formato').classList.add('d-none');
        document.getElementById('cores-acoes').classList.add('d-none');
        return;
    }

    paletas.forEach(b => { b.disabled = false; });

    if (!dados.logoDisponivel) {
        document.getElementById('logo-ajuda').textContent =
            'O logo próprio faz parte do plano Escritório (contas de contador): seus clientes veem a marca do escritório no sistema.';
        document.getElementById('logo-acoes').classList.add('d-none');
        document.getElementById('logo-formato').classList.add('d-none');
    }

    document.querySelector('.config-paletas').addEventListener('click', e => {
        const botao = e.target.closest('.aparencia-paleta');
        if (botao && !botao.disabled) escolherPaleta(botao.dataset.paleta);
    });

    botaoDesfazerCores.addEventListener('click', () => escolherPaleta(paletaSalva));

    botaoSalvarCores.addEventListener('click', async () => {
        await executarComBotaoDesabilitado(botaoSalvarCores, async () => {
            try {
                await apiFetch('/api/aparencia/paleta', { method: 'PUT', body: JSON.stringify({ paleta: paletaEscolhida }) });
                paletaSalva = paletaEscolhida;
                marcarPaleta();
                mostrarToast('Cores da conta salvas: valem para todos os usuários.');
            } catch (err) {
                mostrarErro(err.message);
            }
        });
        marcarPaleta();
    });

    // Saiu da página sem salvar as cores: a pré-visualização não fica.
    window.addEventListener('pagehide', () => { raiz.dataset.paleta = paletaSalva; });

    arquivoLogo.addEventListener('change', async () => {
        const arquivo = arquivoLogo.files[0];
        arquivoLogo.value = '';
        if (!arquivo) return;
        if (arquivo.size > 512 * 1024) {
            mostrarErro('O logo pode ter até 512 KB. Reduza a imagem e envie de novo.');
            return;
        }

        const formulario = new FormData();
        formulario.append('arquivo', arquivo);
        try {
            // Multipart: fetch direto (apiFetch manda JSON).
            const resposta = await fetch('/api/aparencia/logo', {
                method: 'PUT', credentials: 'same-origin', headers: obterCsrfHeader(), body: formulario
            });
            const corpo = await resposta.json().catch(() => null);
            if (!resposta.ok) throw new Error(extrairMensagemDeErro(corpo) ?? 'Não foi possível enviar o logo.');
            aplicarLogo(corpo.versao);
            mostrarToast('Logo atualizado.');
        } catch (err) {
            mostrarErro(err.message);
        }
    });

    botaoRemover.addEventListener('click', async () => {
        await executarComBotaoDesabilitado(botaoRemover, async () => {
            try {
                await apiFetch('/api/aparencia/logo', { method: 'DELETE' });
                aplicarLogo(null);
                mostrarToast('Logo removido: voltou a marca do micoempresa.');
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });
});
