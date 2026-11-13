// Configurações da conta — uma seção por assunto, sem misturar:
//   - Dados da conta: cadastro (só leitura) e WhatsApp do responsável;
//   - Logomarca (B9);
//   - E-mail da nota: só o atalho para Modelos de e-mail (modelos-email.js).
// Só o administrador muda (o servidor confere de novo); os demais veem só
// para leitura. As cores do sistema NÃO ficam aqui: continuam no painel
// Aparência (aparencia.js).

// ---------- Dados da conta ----------
document.addEventListener('DOMContentLoaded', async function () {
    const form = document.getElementById('form-contato-conta');
    if (!form) return;
    const campo = document.getElementById('whatsapp-conta');
    const autoriza = document.getElementById('autoriza-whatsapp-conta');
    aplicarMascaraWhatsApp(campo);

    const formatarDocumento = d => d.length === 14
        ? `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`
        : d.length === 11 ? `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}` : d;

    function mostrar(dados) {
        document.getElementById('dado-nome').textContent = dados.nome;
        document.getElementById('dado-documento').textContent = formatarDocumento(dados.documento);
        document.getElementById('dado-email').textContent = dados.emailResponsavel ?? '—';
        campo.value = dados.whatsApp ?? '';
        autoriza.checked = !!dados.whatsAppAutorizado;
        autoriza.disabled = !dados.podeAlterar || campo.value.trim() === '';
    }

    let dados;
    try {
        dados = await apiFetch('/api/conta/dados');
    } catch (err) {
        mostrarErro(err.message);
        return;
    }
    mostrar(dados);

    if (!dados.podeAlterar) {
        campo.disabled = true;
        document.getElementById('acoes-contato-conta').classList.add('d-none');
        return;
    }

    // Autorização só faz sentido com número.
    campo.addEventListener('input', () => {
        autoriza.disabled = campo.value.trim() === '';
        if (autoriza.disabled) autoriza.checked = false;
    });

    form.addEventListener('submit', async e => {
        e.preventDefault();
        const botao = document.getElementById('btn-salvar-contato');
        await executarComBotaoDesabilitado(botao, async () => {
            try {
                const salvo = await apiFetch('/api/conta/contato', {
                    method: 'PUT',
                    body: JSON.stringify({ whatsApp: campo.value.trim() || null, autorizaContato: autoriza.checked })
                });
                mostrar(salvo);
                mostrarToast(salvo.whatsApp ? 'WhatsApp salvo.' : 'WhatsApp removido.');
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });
});

// ---------- Logomarca (B9) ----------
document.addEventListener('DOMContentLoaded', async function () {
    const previa = document.getElementById('logo-previa');
    const botaoRemover = document.getElementById('btn-remover-logo');
    const arquivoLogo = document.getElementById('arquivo-logo');

    function urlLogo(versao) {
        return versao ? `/api/aparencia/logo?v=${versao}` : null;
    }

    function mostrarPrevia(versao) {
        previa.innerHTML = versao
            ? `<img src="${urlLogo(versao)}" alt="Logomarca da conta" />`
            : '<span class="text-muted small">Sem logomarca: usando a marca do micoempresa.</span>';
        botaoRemover.classList.toggle('d-none', !versao);
    }

    // Depois de enviar/remover: prévia, impressão e menu. Trocar uma
    // logomarca por outra troca a imagem do menu na hora; passar de "sem"
    // para "com" (ou o contrário) muda o topo do menu, que vem pronto do
    // servidor — recarrega a página.
    function aplicarLogo(versao) {
        mostrarPrevia(versao);
        const url = urlLogo(versao);
        if (url) document.body.dataset.logoConta = url; else delete document.body.dataset.logoConta;

        const noMenu = document.getElementById('logo-conta-menu');
        if (noMenu && url) noMenu.src = url;
        if (!!noMenu !== !!url) setTimeout(() => location.reload(), 700);
    }

    let dados;
    try {
        dados = await apiFetch('/api/aparencia');
    } catch (err) {
        mostrarErro(err.message);
        return;
    }

    mostrarPrevia(dados.logoVersao);

    if (!dados.podeAlterarPaleta) {
        // Usuário comum: só vê.
        document.getElementById('config-conta-intro').textContent =
            'Vale para todos os usuários desta conta. Só o administrador da conta pode mudar.';
        document.getElementById('logo-acoes').classList.add('d-none');
        document.getElementById('logo-formato').classList.add('d-none');
        return;
    }

    if (!dados.logoDisponivel) {
        document.getElementById('logo-ajuda').textContent =
            'A logomarca própria faz parte do plano Escritório (contas de contador): seus clientes veem a marca do escritório no sistema.';
        document.getElementById('logo-acoes').classList.add('d-none');
        document.getElementById('logo-formato').classList.add('d-none');
        return;
    }

    arquivoLogo.addEventListener('change', async () => {
        const arquivo = arquivoLogo.files[0];
        arquivoLogo.value = '';
        if (!arquivo) return;
        if (arquivo.size > 512 * 1024) {
            mostrarErro('A logomarca pode ter até 512 KB. Reduza a imagem e envie de novo.');
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
            if (!resposta.ok) throw new Error(extrairMensagemDeErro(corpo) ?? 'Não foi possível enviar a logomarca.');
            aplicarLogo(corpo.versao);
            mostrarToast('Logomarca atualizada.');
        } catch (err) {
            mostrarErro(err.message);
        }
    });

    botaoRemover.addEventListener('click', async () => {
        await executarComBotaoDesabilitado(botaoRemover, async () => {
            try {
                await apiFetch('/api/aparencia/logo', { method: 'DELETE' });
                aplicarLogo(null);
                mostrarToast('Logomarca removida: voltou a marca do micoempresa.');
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });
});

