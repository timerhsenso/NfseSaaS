// Configurações da conta — uma seção por assunto, sem misturar:
//   - Dados da conta: cadastro (só leitura) e WhatsApp do responsável;
//   - Segurança (SEG1): exigir a verificação em duas etapas de todos;
//   - Logomarca (B9);
//   - E-mail da nota: só o atalho para Modelos de e-mail (modelos-email.js);
//   - Documentos aceitos (JUR1): lista e comprovante em PDF.
// Só o administrador muda (o servidor confere de novo); os demais veem só
// para leitura. As cores do sistema NÃO ficam aqui: continuam no painel
// Aparência (aparencia.js).

// "12345678000195" → "12.345.678/0001-95"; CPF → "123.456.789-09".
function formatarDocumento(d) {
    d = d ?? '';
    return d.length === 14
        ? `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`
        : d.length === 11 ? `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}` : d;
}

// ---------- Dados da conta ----------
document.addEventListener('DOMContentLoaded', async function () {
    const form = document.getElementById('form-contato-conta');
    if (!form) return;
    const campo = document.getElementById('whatsapp-conta');
    const autoriza = document.getElementById('autoriza-whatsapp-conta');
    aplicarMascaraWhatsApp(campo);

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

// ---------- Segurança (SEG1) ----------
document.addEventListener('DOMContentLoaded', async function () {
    const card = document.getElementById('seguranca');
    if (!card) return;
    const chave = document.getElementById('seguranca-exigir-todos');
    const ligueASua = document.getElementById('seguranca-ligue-a-sua');

    function mostrar(dados) {
        card.classList.remove('d-none');
        document.getElementById('seguranca-prazo').textContent = dados.diasPrazoAdesao;
        chave.checked = dados.exigirDeTodos;
        // Exigir pede a sua ligada; desligar a exigência sempre pode.
        chave.disabled = !dados.exigirDeTodos && !dados.voceLigou;
        ligueASua.classList.toggle('d-none', dados.voceLigou || dados.exigirDeTodos);
        document.getElementById('seguranca-desde').textContent = dados.exigidoDesde
            ? `Exigida de todos desde ${new Date(dados.exigidoDesde).toLocaleDateString('pt-BR')}.`
            : '';
        const plural = dados.usuarios === 1 ? 'usuário' : 'usuários';
        document.getElementById('seguranca-contagem').textContent = dados.usuarios === dados.usuariosComDuploFator
            ? (dados.usuarios === 1 ? 'O único usuário da conta já ligou.' : `Todos os ${dados.usuarios} usuários já ligaram.`)
            : dados.usuarios === 1
                ? 'O único usuário da conta ainda não ligou.'
                : `${dados.usuariosComDuploFator} de ${dados.usuarios} ${plural} já ligaram. Ainda sem:`;
        const lista = document.getElementById('seguranca-pendentes');
        lista.classList.toggle('d-none', dados.usuarios === 1);
        lista.replaceChildren(...dados.usuariosSemDuploFator.map(email => {
            const li = document.createElement('li');
            li.textContent = email;
            return li;
        }));
    }

    try {
        mostrar(await apiFetch('/api/conta/seguranca'));
    } catch {
        return; // não é administrador: a seção fica escondida
    }

    // Ligou a própria verificação pelo menu do usuário: atualiza a seção.
    document.getElementById('modalDuploFator')?.addEventListener('hidden.bs.modal', async () => {
        try { mostrar(await apiFetch('/api/conta/seguranca')); } catch { /* segue como estava */ }
    });

    chave.addEventListener('change', async () => {
        const exigir = chave.checked;
        if (!exigir && !await confirmarAcao('Deixar de exigir dos outros usuários? Os administradores continuam obrigados.')) {
            chave.checked = true;
            return;
        }
        chave.disabled = true;
        try {
            const salvo = await apiFetch('/api/conta/seguranca', { method: 'PUT', body: JSON.stringify({ exigirDeTodos: exigir }) });
            mostrar(salvo);
            mostrarToast(exigir ? 'Verificação em duas etapas exigida de todos os usuários.' : 'Exigência para todos desligada.');
        } catch (err) {
            chave.checked = !exigir;
            chave.disabled = false;
            mostrarErro(err.message);
        }
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
        // PL1: recurso fechado aparece com cadeado e o caminho para liberar.
        document.getElementById('logo-ajuda').innerHTML =
            '<i class="bi bi-lock-fill text-muted" aria-hidden="true"></i> A logomarca própria não está incluída no seu plano: com ela, seus clientes veem a sua marca no sistema. ' +
            '<a href="/Assinatura">Conhecer os planos</a>';
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
            // Multipart com progresso no botão "Enviar logomarca" (um <label>).
            const corpo = await enviarArquivoComProgresso('/api/aparencia/logo', formulario, {
                metodo: 'PUT', botao: document.querySelector('label[for="arquivo-logo"]')
            });
            aplicarLogo(corpo?.versao);
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


// ---------- Documentos aceitos (JUR1) ----------
// Administrador: a conta toda; os demais: os próprios aceites (o servidor filtra).
document.addEventListener('DOMContentLoaded', async function () {
    const tabela = document.getElementById('tabela-documentos-aceitos');
    if (!tabela) return;
    const corpo = tabela.querySelector('tbody');

    let itens;
    try {
        itens = await apiFetch('/api/legal/aceitos');
    } catch (err) {
        corpo.innerHTML = `<tr><td colspan="6" class="text-danger small">${escaparHtml(err.message)}</td></tr>`;
        return;
    }
    if (!itens.length) {
        corpo.innerHTML = '<tr><td colspan="6" class="text-muted small">Nenhum documento aceito ainda.</td></tr>';
        return;
    }

    const quando = iso => new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
    // Aceite de versão mais nova do mesmo documento (mesma pessoa e empresa) substitui o anterior.
    const ordem = v => v.split('.').map(Number).reduce((total, n) => total * 1000 + n, 0);
    const chave = d => `${d.tipo}|${d.usuarioEmail}|${d.cnpjEmpresa ?? ''}`;
    const maisNova = new Map();
    for (const d of itens.filter(x => !x.revogadoEm))
        maisNova.set(chave(d), Math.max(maisNova.get(chave(d)) ?? 0, ordem(d.versao)));
    corpo.innerHTML = itens.map(d => {
        const empresa = d.empresa ? `<div class="small text-muted">${escaparHtml(d.empresa)}${d.cnpjEmpresa ? ` · ${escaparHtml(formatarDocumento(d.cnpjEmpresa))}` : ''}</div>` : '';
        const situacao = d.revogadoEm
            ? `<span class="badge text-bg-secondary" title="${escaparHtml(d.motivoRevogacao ?? '')}">Revogado em ${quando(d.revogadoEm)}</span>`
            : ordem(d.versao) < maisNova.get(chave(d))
                ? '<span class="badge text-bg-light border" title="Há aceite de versão mais nova deste documento.">Substituído</span>'
                : '<span class="badge text-bg-success">Vigente</span>';
        const hash = d.hashTexto ? ` title="SHA-256 do texto: ${escaparHtml(d.hashTexto)}"` : '';
        return `<tr>
            <td${hash}><a href="${escaparHtml(d.url)}" target="_blank" rel="noopener" class="${d.revogadoEm ? 'revogado' : ''}">${escaparHtml(d.nome)}</a>${empresa}</td>
            <td class="col-curta">${escaparHtml(d.versao)}</td>
            <td><span class="small">${escaparHtml(d.usuarioEmail)}</span>${d.ip ? `<div class="small text-muted">IP ${escaparHtml(d.ip)}</div>` : ''}</td>
            <td class="col-curta">${quando(d.aceitoEm)}</td>
            <td class="col-curta">${situacao}</td>
            <td class="col-curta text-end">
                <a class="btn btn-outline-secondary btn-sm" href="/api/legal/aceitos/${d.origem}/${d.id}/comprovante" title="Baixar comprovante em PDF">
                    <i class="bi bi-file-earmark-pdf" aria-hidden="true"></i><span class="visually-hidden">Comprovante</span>
                </a>
            </td>
        </tr>`;
    }).join('');
});
