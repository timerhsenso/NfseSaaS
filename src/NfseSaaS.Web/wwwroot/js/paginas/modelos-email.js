// Modelos de e-mail — modelos reutilizáveis do e-mail da nota. Lista à
// esquerda, editor e prévia à direita. Os campos entre colchetes
// ("[Número da nota]") viram os dados de cada nota no envio; a prévia troca
// pelos exemplos que a API manda (mesma troca do servidor,
// RegrasModeloEmailNota.Preencher). Só o administrador grava.

const REGEX_CAMPO = /\[[^\[\]\r\n]{1,40}\]/g;

let dadosModelos = null;     // última resposta de GET /api/modelos-email-nota
let modeloAtualId = null;    // null = modelo novo (ainda não salvo)
let campoAtivoModelo = null;
let alteradoSemSalvar = false;

document.addEventListener('DOMContentLoaded', async function () {
    campoAtivoModelo = el('modelo-mensagem');
    ['modelo-assunto', 'modelo-mensagem'].forEach(id => el(id).addEventListener('focus', () => { campoAtivoModelo = el(id); }));
    ['modelo-nome', 'modelo-assunto', 'modelo-mensagem', 'modelo-responder', 'modelo-cco'].forEach(id =>
        el(id).addEventListener('input', () => { alteradoSemSalvar = true; atualizarPrevia(); }));
    el('modelo-ocultar-divulgacao').addEventListener('change', () => { alteradoSemSalvar = true; atualizarPrevia(); });
    el('modelo-ocultar-saida-copias').addEventListener('change', () => { alteradoSemSalvar = true; atualizarPrevia(); });

    el('modelo-campos').addEventListener('click', e => {
        const botao = e.target.closest('[data-etiqueta]');
        if (botao) inserirCampo(botao.dataset.etiqueta);
    });
    el('lista-modelos').addEventListener('click', async e => {
        const item = e.target.closest('[data-id]');
        if (!item || item.dataset.id === modeloAtualId) return;
        if (alteradoSemSalvar && !await confirmarAcao('Há alterações não salvas neste modelo. Descartar e abrir o outro?',
            { titulo: 'Descartar alterações', textoBotao: 'Descartar' })) return;
        abrirModelo(item.dataset.id);
    });

    el('btn-novo-modelo').addEventListener('click', novoModelo);
    el('btn-texto-padrao').addEventListener('click', () => {
        el('modelo-assunto').value = dadosModelos.assuntoPadrao;
        el('modelo-mensagem').value = dadosModelos.mensagemPadrao;
        alteradoSemSalvar = true;
        atualizarPrevia();
        mostrarToast('Texto padrão colocado. Clique em Salvar para manter.');
    });
    el('form-modelo').addEventListener('submit', async e => {
        e.preventDefault();
        await executarComBotaoDesabilitado(el('btn-salvar-modelo'), salvarModelo);
    });
    el('btn-teste-modelo').addEventListener('click', e => executarComBotaoDesabilitado(e.currentTarget, enviarTeste));
    el('btn-duplicar-modelo').addEventListener('click', duplicarModelo);
    el('btn-padrao-modelo').addEventListener('click', tornarPadrao);
    el('btn-excluir-modelo').addEventListener('click', excluirModelo);

    await carregarModelos();
});

function el(id) {
    return document.getElementById(id);
}

async function carregarModelos(abrirId) {
    try {
        dadosModelos = await apiFetch('/api/modelos-email-nota');
    } catch (err) {
        mostrarErro(err.message);
        return;
    }

    el('modelo-campos').innerHTML = dadosModelos.campos.map(c =>
        `<button type="button" class="btn btn-outline-secondary btn-sm email-nota-campo" data-etiqueta="${escaparHtml(c.etiqueta)}" title="${escaparHtml(c.descricao)}">${escaparHtml(c.etiqueta)}</button>`).join('');
    el('btn-novo-modelo').classList.toggle('d-none', !dadosModelos.podeAlterar);
    // PL1: sempre visível; fechado no plano = desmarcado, desabilitado e com cadeado.
    el('modelo-divulgacao-caixa').classList.remove('d-none');
    el('modelo-ocultar-divulgacao').disabled = !dadosModelos.podeOcultarDivulgacao;
    el('modelo-divulgacao-bloqueada').classList.toggle('d-none', !!dadosModelos.podeOcultarDivulgacao);
    if (!dadosModelos.podeAlterar)
        el('modelos-intro').insertAdjacentHTML('beforeend', ' <strong>Só o administrador da conta cria e altera modelos.</strong>');

    renderizarLista();
    const alvo = dadosModelos.modelos.find(m => m.id === abrirId) ?? dadosModelos.modelos.find(m => m.id === modeloAtualId) ?? dadosModelos.modelos[0];
    if (alvo) abrirModelo(alvo.id);
}

function usoDoModelo(m) {
    const partes = [];
    if (m.empresas.length) partes.push(`${m.empresas.length === 1 ? 'Empresa' : 'Empresas'}: ${m.empresas.map(escaparHtml).join(', ')}`);
    if (m.clientes) partes.push(`${m.clientes} ${m.clientes === 1 ? 'cliente' : 'clientes'}`);
    if (m.padrao) partes.unshift('Quem não escolheu outro');
    return partes.length ? partes.join(' · ') : 'Ninguém usa ainda';
}

function renderizarLista() {
    el('lista-modelos').innerHTML = dadosModelos.modelos.map(m => `
        <button type="button" class="list-group-item list-group-item-action ${m.id === modeloAtualId ? 'active' : ''}" data-id="${m.id}">
            <div class="d-flex align-items-center gap-2">
                <span class="fw-semibold text-truncate">${escaparHtml(m.nome)}</span>
                ${m.padrao ? '<span class="badge text-bg-warning ms-auto"><i class="bi bi-star-fill"></i> Padrão</span>' : ''}
            </div>
            <div class="small text-truncate ${m.id === modeloAtualId ? '' : 'text-muted'}">${escaparHtml(m.assunto)}</div>
            <div class="small ${m.id === modeloAtualId ? '' : 'text-muted'}"><i class="bi bi-people"></i> ${usoDoModelo(m)}</div>
        </button>`).join('');
}

function abrirModelo(id) {
    const m = dadosModelos.modelos.find(x => x.id === id);
    if (!m) return;
    modeloAtualId = id;
    el('editor-titulo').textContent = m.nome;
    el('editor-selo-padrao').classList.toggle('d-none', !m.padrao);
    el('editor-uso').innerHTML = `<i class="bi bi-people"></i> ${usoDoModelo(m)}`;
    el('modelo-nome').value = m.nome;
    el('modelo-assunto').value = m.assunto;
    el('modelo-mensagem').value = m.mensagem;
    el('modelo-responder').value = m.responderPara ?? '';
    el('modelo-cco').value = m.copiaOcultaPara ?? '';
    el('modelo-ocultar-divulgacao').checked = !!m.ocultarDivulgacao;
    el('modelo-ocultar-saida-copias').checked = !!m.ocultarSaidaCopias;
    aplicarPermissoes(m);
    alteradoSemSalvar = false;
    renderizarLista();
    atualizarPrevia();
}

function novoModelo() {
    if (dadosModelos.modelos.length >= dadosModelos.maximoModelos) {
        mostrarErro(`A conta pode ter até ${dadosModelos.maximoModelos} modelos. Exclua um que não usa mais.`);
        return;
    }
    modeloAtualId = null;
    el('editor-titulo').textContent = 'Novo modelo';
    el('editor-selo-padrao').classList.add('d-none');
    el('editor-uso').textContent = 'Depois de salvar, escolha este modelo no cadastro da empresa ou do cliente.';
    el('modelo-nome').value = '';
    el('modelo-assunto').value = dadosModelos.assuntoPadrao;
    el('modelo-mensagem').value = dadosModelos.mensagemPadrao;
    el('modelo-responder').value = '';
    el('modelo-cco').value = '';
    el('modelo-ocultar-divulgacao').checked = false;
    el('modelo-ocultar-saida-copias').checked = false;
    aplicarPermissoes(null);
    alteradoSemSalvar = true;
    renderizarLista();
    atualizarPrevia();
    el('modelo-nome').focus();
}

function aplicarPermissoes(modelo) {
    const pode = !!dadosModelos.podeAlterar;
    ['modelo-nome', 'modelo-assunto', 'modelo-mensagem', 'modelo-responder', 'modelo-cco']
        .forEach(id => { el(id).disabled = !pode; });
    el('modelo-ocultar-divulgacao').disabled = !pode || !dadosModelos.podeOcultarDivulgacao;
    el('modelo-ocultar-saida-copias').disabled = !pode;
    document.querySelectorAll('#modelo-campos button').forEach(b => { b.disabled = !pode; });
    el('btn-salvar-modelo').classList.toggle('d-none', !pode);
    el('btn-texto-padrao').classList.toggle('d-none', !pode);
    el('editor-mais').classList.toggle('d-none', !pode || !modelo);
    if (modelo) {
        el('btn-padrao-modelo').closest('li').classList.toggle('d-none', modelo.padrao);
        el('btn-excluir-modelo').disabled = modelo.padrao;
        el('btn-excluir-modelo').title = modelo.padrao ? 'O modelo padrão não pode ser excluído. Marque outro como padrão antes.' : '';
    }
}

function preencher(texto) {
    return String(texto ?? '').replace(REGEX_CAMPO, etiqueta => {
        const campo = dadosModelos.campos.find(c => c.etiqueta.toLowerCase() === etiqueta.toLowerCase());
        return campo ? campo.exemplo : etiqueta;
    });
}

function exemplo(etiqueta) {
    return dadosModelos.campos.find(c => c.etiqueta === etiqueta)?.exemplo ?? '';
}

function atualizarPrevia() {
    if (!dadosModelos) return;
    const assunto = el('modelo-assunto').value;
    const mensagem = el('modelo-mensagem').value;
    el('previa-assunto').textContent = preencher(assunto) || '(sem assunto)';
    const paragrafos = preencher(mensagem).replace(/\r\n/g, '\n').trim().split(/\n\s*\n/)
        .map(p => `<p>${p.split('\n').map(escaparHtml).join('<br>')}</p>`).join('');
    const semRodape = el('modelo-ocultar-divulgacao').checked && dadosModelos.podeOcultarDivulgacao;
    el('previa-corpo').innerHTML = paragrafos +
        `<div class="email-nota-fixo small">
            <table><tbody>
                <tr><td class="text-secondary">Número</td><td><strong>${escaparHtml(exemplo('[Número da nota]'))}</strong></td></tr>
                <tr><td class="text-secondary">Emissão</td><td>${escaparHtml(exemplo('[Data de emissão]'))}</td></tr>
                <tr><td class="text-secondary">Competência</td><td>${escaparHtml(exemplo('[Competência]'))}</td></tr>
                <tr><td class="text-secondary">Valor do serviço</td><td>${escaparHtml(exemplo('[Valor]'))}</td></tr>
            </tbody></table>
            <div class="mt-1">Confira a autenticidade da nota na consulta pública da NFS-e.</div>
            <div class="mt-1"><i class="bi bi-paperclip"></i> PDF (DANFSe) e XML anexados</div>
            ${el('modelo-ocultar-saida-copias').checked ? '' : `<div class="email-nota-rodape mt-2">Recebeu como cópia e não quer mais as notas de ${escaparHtml(exemplo('[Empresa]'))}? <u>Pedir para sair das cópias</u> <span class="text-muted">(só quando a nota vai com cópia)</span></div>`}
            <div class="email-nota-rodape mt-2">Enviado pela plataforma micoempresa em nome de ${escaparHtml(exemplo('[Empresa]'))}.</div>
            ${semRodape ? '' : '<div class="email-nota-rodape">Você também presta serviço? Emita suas notas com o micoempresa… Crie sua conta grátis.</div>'}
        </div>`;

    const todos = `${assunto}\n${mensagem}`.match(REGEX_CAMPO) ?? [];
    const conhecidos = dadosModelos.campos.map(c => c.etiqueta.toLowerCase());
    const desconhecidos = [...new Set(todos.filter(t => !conhecidos.includes(t.toLowerCase())))];
    el('modelo-aviso-campos').innerHTML = desconhecidos.length
        ? `<span class="text-warning-emphasis"><i class="bi bi-exclamation-triangle"></i> ${desconhecidos.map(escaparHtml).join(', ')} não é campo do sistema e sai como está escrito. Use os botões acima.</span>`
        : 'Abaixo da mensagem seguem sempre os dados da nota, a consulta pública e os anexos.';
    el('btn-teste-modelo').disabled = !assunto.trim() || !mensagem.trim();
}

function inserirCampo(etiqueta) {
    const c = campoAtivoModelo;
    if (c.disabled) return;
    const inicio = c.selectionStart ?? c.value.length;
    const fim = c.selectionEnd ?? c.value.length;
    c.value = c.value.slice(0, inicio) + etiqueta + c.value.slice(fim);
    c.focus();
    c.setSelectionRange(inicio + etiqueta.length, inicio + etiqueta.length);
    alteradoSemSalvar = true;
    atualizarPrevia();
}

function corpoModelo() {
    return {
        nome: el('modelo-nome').value.trim(),
        assunto: el('modelo-assunto').value.trim(),
        mensagem: el('modelo-mensagem').value,
        responderPara: el('modelo-responder').value.trim() || null,
        copiaOcultaPara: el('modelo-cco').value.trim() || null,
        ocultarDivulgacao: el('modelo-ocultar-divulgacao').checked,
        ocultarSaidaCopias: el('modelo-ocultar-saida-copias').checked
    };
}

async function salvarModelo() {
    try {
        let id = modeloAtualId;
        if (id) {
            await apiFetch(`/api/modelos-email-nota/${id}`, { method: 'PUT', body: JSON.stringify(corpoModelo()) });
        } else {
            id = (await apiFetch('/api/modelos-email-nota', { method: 'POST', body: JSON.stringify(corpoModelo()) })).id;
        }
        alteradoSemSalvar = false;
        modeloAtualId = id;
        await carregarModelos(id);
        mostrarToast('Modelo salvo. Vale para as próximas notas enviadas.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function enviarTeste() {
    try {
        const r = await apiFetch('/api/modelos-email-nota/teste', {
            method: 'POST',
            body: JSON.stringify({
                assunto: el('modelo-assunto').value.trim(),
                mensagem: el('modelo-mensagem').value,
                responderPara: el('modelo-responder').value.trim() || null,
                ocultarDivulgacao: el('modelo-ocultar-divulgacao').checked
            })
        });
        mostrarToast(`Teste enviado para ${r.email}. Confira a caixa de entrada em alguns instantes.`);
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function duplicarModelo() {
    try {
        const r = await apiFetch(`/api/modelos-email-nota/${modeloAtualId}/duplicar`, { method: 'POST' });
        await carregarModelos(r.id);
        mostrarToast('Modelo duplicado. Mude o nome e o texto e salve.');
        el('modelo-nome').focus();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function tornarPadrao() {
    const m = dadosModelos.modelos.find(x => x.id === modeloAtualId);
    if (!await confirmarAcao(`"${m.nome}" passa a ser o modelo de quem não escolheu outro. Continuar?`,
        { titulo: 'Tornar padrão da conta', textoBotao: 'Tornar padrão' })) return;
    try {
        await apiFetch(`/api/modelos-email-nota/${modeloAtualId}/padrao`, { method: 'POST' });
        await carregarModelos(modeloAtualId);
        mostrarToast(`"${m.nome}" agora é o modelo padrão.`);
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function excluirModelo() {
    const m = dadosModelos.modelos.find(x => x.id === modeloAtualId);
    const padrao = dadosModelos.modelos.find(x => x.padrao);
    const usam = [];
    if (m.empresas.length) usam.push(`${m.empresas.length} ${m.empresas.length === 1 ? 'empresa' : 'empresas'}`);
    if (m.clientes) usam.push(`${m.clientes} ${m.clientes === 1 ? 'cliente' : 'clientes'}`);
    const mensagem = usam.length
        ? `"${m.nome}" é usado por ${usam.join(' e ')}. Eles passam a usar o modelo padrão ("${padrao?.nome}"). Excluir?`
        : `Excluir o modelo "${m.nome}"?`;
    if (!await confirmarAcao(mensagem, { titulo: 'Excluir modelo', textoBotao: 'Excluir', variante: 'perigo' })) return;
    try {
        await apiFetch(`/api/modelos-email-nota/${modeloAtualId}`, { method: 'DELETE' });
        modeloAtualId = null;
        alteradoSemSalvar = false;
        await carregarModelos();
        mostrarToast('Modelo excluído.');
    } catch (err) {
        mostrarErro(err.message);
    }
}
