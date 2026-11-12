// Configurações da conta — uma seção por assunto, sem misturar:
//   - Dados da conta: cadastro (só leitura) e WhatsApp do responsável;
//   - Logomarca (B9);
//   - E-mail da nota: texto do e-mail que leva a nota ao cliente.
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

// ---------- E-mail da nota ----------
// Modelo da conta ou personalizado por empresa. Os campos entre colchetes
// ("[Número da nota]") viram os dados de cada nota no envio; a prévia
// troca pelos exemplos que a API manda (mesma troca do servidor,
// RegrasModeloEmailNota.Preencher).
document.addEventListener('DOMContentLoaded', async function () {
    const form = document.getElementById('form-email-nota');
    if (!form) return;

    const campoAssunto = document.getElementById('email-nota-assunto');
    const campoMensagem = document.getElementById('email-nota-mensagem');
    const campoResponder = document.getElementById('email-nota-responder');
    const campoCco = document.getElementById('email-nota-cco');
    const ocultarDivulgacao = document.getElementById('email-nota-ocultar-divulgacao');
    const seletorEmpresa = document.getElementById('email-nota-empresa');
    const personalizar = document.getElementById('email-nota-personalizar');

    let modelo = null;          // última resposta da API
    let campoAtivo = campoMensagem;

    [campoAssunto, campoMensagem].forEach(c => c.addEventListener('focus', () => { campoAtivo = c; }));

    function preencher(texto) {
        return String(texto ?? '').replace(/\[[^\[\]\r\n]{1,40}\]/g, etiqueta => {
            const campo = (modelo?.campos ?? []).find(c => c.etiqueta.toLowerCase() === etiqueta.toLowerCase());
            return campo ? campo.exemplo : etiqueta;
        });
    }

    function exemplo(etiqueta) {
        return (modelo?.campos ?? []).find(c => c.etiqueta === etiqueta)?.exemplo ?? '';
    }

    function camposDesconhecidos() {
        const todos = `${campoAssunto.value}\n${campoMensagem.value}`.match(/\[[^\[\]\r\n]{1,40}\]/g) ?? [];
        const conhecidos = (modelo?.campos ?? []).map(c => c.etiqueta.toLowerCase());
        return [...new Set(todos.filter(t => !conhecidos.includes(t.toLowerCase())))];
    }

    function atualizarPrevia() {
        document.getElementById('email-nota-previa-assunto').textContent = preencher(campoAssunto.value) || '(sem assunto)';
        const paragrafos = preencher(campoMensagem.value).replace(/\r\n/g, '\n').trim().split(/\n\s*\n/)
            .map(p => `<p>${p.split('\n').map(escaparHtml).join('<br>')}</p>`).join('');
        const rodape = ocultarDivulgacao.checked && modelo?.podeOcultarDivulgacao
            ? ''
            : '<div class="email-nota-rodape">Você também presta serviço? Emita suas notas com o micoempresa… Crie sua conta grátis.</div>';
        document.getElementById('email-nota-previa-corpo').innerHTML = paragrafos +
            `<div class="email-nota-fixo small">
                <table><tbody>
                    <tr><td class="text-secondary">Número</td><td><strong>${escaparHtml(exemplo('[Número da nota]'))}</strong></td></tr>
                    <tr><td class="text-secondary">Emissão</td><td>${escaparHtml(exemplo('[Data de emissão]'))}</td></tr>
                    <tr><td class="text-secondary">Competência</td><td>${escaparHtml(exemplo('[Competência]'))}</td></tr>
                    <tr><td class="text-secondary">Valor do serviço</td><td>${escaparHtml(exemplo('[Valor]'))}</td></tr>
                </tbody></table>
                <div class="mt-1">Confira a autenticidade da nota na consulta pública da NFS-e.</div>
                <div class="mt-1"><i class="bi bi-paperclip"></i> PDF (DANFSe) e XML anexados</div>
                <div class="email-nota-rodape mt-2">Enviado pela plataforma micoempresa em nome de ${escaparHtml(exemplo('[Empresa]'))}.</div>
                ${rodape}
            </div>`;

        const desconhecidos = camposDesconhecidos();
        const aviso = document.getElementById('email-nota-aviso-campos');
        aviso.innerHTML = desconhecidos.length
            ? `<span class="text-warning-emphasis"><i class="bi bi-exclamation-triangle"></i> ${desconhecidos.map(escaparHtml).join(', ')} não é campo do sistema e sai como está escrito. Use os botões acima.</span>`
            : 'Abaixo da mensagem seguem sempre os dados da nota, a consulta pública e os anexos.';
    }

    function inserirCampo(etiqueta) {
        const c = campoAtivo;
        if (c.disabled) return;
        const inicio = c.selectionStart ?? c.value.length;
        const fim = c.selectionEnd ?? c.value.length;
        c.value = c.value.slice(0, inicio) + etiqueta + c.value.slice(fim);
        c.focus();
        c.setSelectionRange(inicio + etiqueta.length, inicio + etiqueta.length);
        atualizarPrevia();
    }

    function editavel() {
        return !!modelo?.podeAlterar && (modelo.empresaId == null || personalizar.checked);
    }

    function aplicarEstado() {
        const pode = editavel();
        [campoAssunto, campoMensagem, campoResponder, campoCco].forEach(c => { c.disabled = !pode; });
        document.querySelectorAll('#email-nota-campos button').forEach(b => { b.disabled = !pode; });
        document.getElementById('btn-padrao-email-nota').classList.toggle('d-none', !pode);
        document.getElementById('btn-teste-email-nota').disabled = !campoAssunto.value.trim() || !campoMensagem.value.trim();
        document.getElementById('email-nota-usa-conta').classList.toggle('d-none', modelo?.empresaId == null || personalizar.checked);

        const naConta = modelo?.empresaId == null;
        document.getElementById('email-nota-divulgacao-caixa').classList.toggle('d-none', !naConta || !modelo?.podeOcultarDivulgacao);
        ocultarDivulgacao.disabled = !modelo?.podeAlterar;

        const empresa = (modelo?.empresas ?? []).find(e => e.id === modelo?.empresaId);
        document.getElementById('email-nota-responder-ajuda').textContent = empresa?.email
            ? `Em branco: ${empresa.email} (e-mail da empresa).`
            : 'Em branco: o e-mail da empresa que emitiu a nota.';
    }

    function mostrar(dados) {
        modelo = dados;

        campoAssunto.value = dados.assunto;
        campoMensagem.value = dados.mensagem;
        campoResponder.value = dados.responderPara ?? '';
        campoCco.value = dados.copiaOcultaPara ?? '';
        ocultarDivulgacao.checked = !!dados.ocultarDivulgacao;
        personalizar.checked = dados.empresaId != null && dados.personalizado;
        document.getElementById('email-nota-personalizar-caixa').classList.toggle('d-none', dados.empresaId == null);
        personalizar.disabled = !dados.podeAlterar;

        // Escolha da empresa só aparece com mais de uma.
        const escolha = document.getElementById('email-nota-escolha-empresa');
        escolha.classList.toggle('d-none', (dados.empresas ?? []).length < 2);
        seletorEmpresa.innerHTML = '<option value="">Todas as empresas (modelo da conta)</option>' +
            (dados.empresas ?? []).map(e => `<option value="${e.id}">${escaparHtml(e.nome)}${e.personalizado ? ' — personalizado' : ''}</option>`).join('');
        seletorEmpresa.value = dados.empresaId ?? '';

        document.getElementById('email-nota-campos').innerHTML = (dados.campos ?? []).map(c =>
            `<button type="button" class="btn btn-outline-secondary btn-sm email-nota-campo" data-etiqueta="${escaparHtml(c.etiqueta)}" title="${escaparHtml(c.descricao)}">${escaparHtml(c.etiqueta)}</button>`).join('');

        document.getElementById('email-nota-acoes').classList.toggle('d-none', !dados.podeAlterar);
        aplicarEstado();
        atualizarPrevia();
    }

    async function carregar(empresaId) {
        try {
            mostrar(await apiFetch('/api/conta/email-nota' + (empresaId ? `?empresaId=${empresaId}` : '')));
        } catch (err) {
            mostrarErro(err.message);
        }
    }

    document.getElementById('email-nota-campos').addEventListener('click', e => {
        const botao = e.target.closest('[data-etiqueta]');
        if (botao) inserirCampo(botao.dataset.etiqueta);
    });
    [campoAssunto, campoMensagem].forEach(c => c.addEventListener('input', () => { atualizarPrevia(); aplicarEstado(); }));
    ocultarDivulgacao.addEventListener('change', atualizarPrevia);
    seletorEmpresa.addEventListener('change', () => carregar(seletorEmpresa.value || null));
    personalizar.addEventListener('change', aplicarEstado);

    document.getElementById('btn-padrao-email-nota').addEventListener('click', () => {
        campoAssunto.value = modelo.assuntoPadrao;
        campoMensagem.value = modelo.mensagemPadrao;
        atualizarPrevia();
        aplicarEstado();
        mostrarToast('Texto padrão colocado. Clique em Salvar para manter.');
    });

    form.addEventListener('submit', async e => {
        e.preventDefault();
        await executarComBotaoDesabilitado(document.getElementById('btn-salvar-email-nota'), async () => {
            try {
                const salvo = await apiFetch('/api/conta/email-nota', {
                    method: 'PUT',
                    body: JSON.stringify({
                        empresaId: modelo.empresaId,
                        personalizar: modelo.empresaId == null || personalizar.checked,
                        assunto: campoAssunto.value.trim(),
                        mensagem: campoMensagem.value,
                        responderPara: campoResponder.value.trim() || null,
                        copiaOcultaPara: campoCco.value.trim() || null,
                        ocultarDivulgacao: ocultarDivulgacao.checked
                    })
                });
                mostrar(salvo);
                mostrarToast(salvo.empresaId != null && !salvo.personalizado
                    ? 'Esta empresa voltou a usar o modelo da conta.'
                    : 'E-mail da nota salvo. Vale para as próximas notas enviadas.');
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });

    document.getElementById('btn-teste-email-nota').addEventListener('click', async e => {
        await executarComBotaoDesabilitado(e.currentTarget, async () => {
            try {
                const r = await apiFetch('/api/conta/email-nota/teste', {
                    method: 'POST',
                    body: JSON.stringify({ empresaId: modelo.empresaId, assunto: campoAssunto.value.trim(), mensagem: campoMensagem.value })
                });
                mostrarToast(`Teste enviado para ${r.email}. Confira a caixa de entrada em alguns instantes.`);
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    });

    await carregar(null);
    if (location.hash === '#email-nota')
        document.getElementById('email-nota').scrollIntoView({ behavior: 'smooth', block: 'start' });
});
