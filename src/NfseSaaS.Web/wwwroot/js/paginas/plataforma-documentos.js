// Plataforma › Documentos (DOC4): catálogo geral de tipos de documento,
// pacotes por ramo e links oficiais por estado/município (api/plataforma/documentos).
// AbrangenciaDocumento: 1 Nacional, 2 Estadual, 3 Municipal.
const ABRANGENCIA_TEXTO = { 1: 'Nacional', 2: 'Estado', 3: 'Município' };
const UFS_BRASIL = ['AC', 'AL', 'AM', 'AP', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MG', 'MS', 'MT', 'PA', 'PB',
    'PE', 'PI', 'PR', 'RJ', 'RN', 'RO', 'RR', 'RS', 'SC', 'SE', 'SP', 'TO'];

let catalogoAdmin = null;
let tipoEmEdicao = null;     // null = novo
let pacoteEmEdicao = null;   // null = novo
let tiposDoPacote = new Set();
let tipoDosLinks = null;
let modalTipo, modalPacote, modalLinks;

function semAcentoCatalogo(texto) {
    return (texto || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

function dominioDe(link) {
    try { return new URL(link).host; } catch { return link; }
}

async function carregarCatalogoAdmin() {
    try {
        catalogoAdmin = await apiFetch('/api/plataforma/documentos');
    } catch (err) {
        mostrarErro(err.message);
        return;
    }
    const select = document.getElementById('tipo-categoria');
    if (!select.options.length)
        select.innerHTML = catalogoAdmin.categorias.map(c => `<option value="${c.valor}">${escaparHtml(c.nome)}</option>`).join('');
    renderizarTipos();
    renderizarPacotesAdmin();
}

function renderizarTipos() {
    const busca = semAcentoCatalogo(document.getElementById('busca-tipos').value.trim());
    const tipos = catalogoAdmin.tipos.filter(t => !busca || semAcentoCatalogo(`${t.nome} ${t.categoriaNome} ${t.orgao ?? ''}`).includes(busca));
    const corpo = document.querySelector('#tabela-tipos tbody');
    if (tipos.length === 0) {
        corpo.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">Nenhum documento.</td></tr>';
        return;
    }
    corpo.innerHTML = tipos.map(t => {
        const situacao = [];
        situacao.push(t.ativo ? '<span class="badge text-bg-success">Ativo</span>' : '<span class="badge text-bg-secondary">Inativo</span>');
        if (t.padraoNovasEmpresas) situacao.push('<span class="badge text-bg-primary">Padrão</span>');
        if (t.exigeValidade) situacao.push('<span class="badge text-bg-light border">Validade obrigatória</span>');
        const onde = t.abrangencia === 1
            ? (t.linkEmissao ? `<a href="${escaparHtml(t.linkEmissao)}" target="_blank" rel="noopener">${escaparHtml(dominioDe(t.linkEmissao))}</a>` : '<span class="text-muted">Nacional, sem link</span>')
            : `${ABRANGENCIA_TEXTO[t.abrangencia]} <span class="text-muted small">(${t.linksLocais} ${t.linksLocais === 1 ? 'link' : 'links'})</span>`;
        return `<tr>
            <td><strong>${escaparHtml(t.nome)}</strong>${t.sistema ? ' <span class="badge text-bg-light border" title="Tipo de sistema (número fixo)">sistema</span>' : ''}
                ${t.orgao ? `<div class="small text-muted">${escaparHtml(t.orgao)}</div>` : ''}</td>
            <td>${escaparHtml(t.categoriaNome)}</td>
            <td>${onde}</td>
            <td class="text-end">${t.validadeDias ? `${t.validadeDias} dias` : '<span class="text-muted">—</span>'}</td>
            <td class="text-end">${t.empresasUsando}</td>
            <td><div class="d-flex flex-wrap gap-1">${situacao.join('')}</div></td>
            <td class="text-end text-nowrap">
                ${t.abrangencia !== 1 ? `<button type="button" class="btn btn-sm btn-outline-secondary" data-acao="links" data-tipo="${t.tipo}"><i class="bi bi-link-45deg"></i> Links</button>` : ''}
                <button type="button" class="btn btn-sm btn-outline-primary" data-acao="editar" data-tipo="${t.tipo}"><i class="bi bi-pencil"></i> Editar</button>
            </td>
        </tr>`;
    }).join('');
}

function renderizarPacotesAdmin() {
    const nomes = Object.fromEntries(catalogoAdmin.tipos.map(t => [t.tipo, t.nome]));
    const corpo = document.querySelector('#tabela-pacotes tbody');
    if (catalogoAdmin.pacotes.length === 0) {
        corpo.innerHTML = '<tr><td colspan="4" class="text-center text-muted py-4">Nenhum pacote.</td></tr>';
        return;
    }
    corpo.innerHTML = catalogoAdmin.pacotes.map(p => `<tr>
        <td><strong>${escaparHtml(p.nome)}</strong>${p.descricao ? `<div class="small text-muted">${escaparHtml(p.descricao)}</div>` : ''}</td>
        <td><span class="badge text-bg-light border">${p.tipos.length}</span>
            <span class="small text-muted">${escaparHtml(p.tipos.map(t => nomes[t]).filter(Boolean).slice(0, 6).join(', '))}${p.tipos.length > 6 ? '…' : ''}</span></td>
        <td>${p.ativo ? '<span class="badge text-bg-success">Ativo</span>' : '<span class="badge text-bg-secondary">Inativo</span>'}</td>
        <td class="text-end"><button type="button" class="btn btn-sm btn-outline-primary" data-acao="editar-pacote" data-id="${p.id}"><i class="bi bi-pencil"></i> Editar</button></td>
    </tr>`).join('');
}

// ---------- Tipo ----------

function atualizarCampoLink() {
    document.getElementById('grupo-tipo-link').classList.toggle('d-none', document.getElementById('tipo-abrangencia').value !== '1');
}

function abrirTipo(tipo) {
    tipoEmEdicao = tipo;
    ocultarErroFormulario('erro-tipo');
    document.getElementById('titulo-modal-tipo').textContent = tipo ? `Editar — ${tipo.nome}` : 'Novo documento';
    document.getElementById('tipo-nome').value = tipo?.nome ?? '';
    document.getElementById('tipo-categoria').value = String(tipo?.categoria ?? catalogoAdmin.categorias[0].valor);
    document.getElementById('tipo-abrangencia').value = String(tipo?.abrangencia ?? 1);
    document.getElementById('tipo-orgao').value = tipo?.orgao ?? '';
    document.getElementById('tipo-link').value = tipo?.linkEmissao ?? '';
    document.getElementById('tipo-validade').value = tipo?.validadeDias ?? '';
    document.getElementById('tipo-ordem').value = tipo?.ordem ?? 100;
    document.getElementById('tipo-exige-validade').checked = tipo?.exigeValidade ?? false;
    document.getElementById('tipo-padrao').checked = tipo?.padraoNovasEmpresas ?? false;
    document.getElementById('tipo-ativo').checked = tipo?.ativo ?? true;
    // "Outro documento" é do sistema: sempre ativo e fora do padrão.
    const outro = tipo?.tipo === 99;
    document.getElementById('tipo-padrao').disabled = outro;
    document.getElementById('tipo-ativo').disabled = outro;
    atualizarCampoLink();
    modalTipo.show();
}

async function salvarTipo(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-tipo');
    const validade = document.getElementById('tipo-validade').value.trim();
    const abrangencia = Number(document.getElementById('tipo-abrangencia').value);
    const corpo = {
        nome: document.getElementById('tipo-nome').value,
        categoria: Number(document.getElementById('tipo-categoria').value),
        abrangencia,
        orgao: document.getElementById('tipo-orgao').value,
        linkEmissao: abrangencia === 1 ? document.getElementById('tipo-link').value : null,
        validadeDias: validade === '' ? null : Number(validade),
        exigeValidade: document.getElementById('tipo-exige-validade').checked,
        ativo: document.getElementById('tipo-ativo').checked,
        padraoNovasEmpresas: document.getElementById('tipo-padrao').checked,
        ordem: Number(document.getElementById('tipo-ordem').value || 0)
    };
    const botao = e.submitter || e.target.querySelector('[type="submit"]');
    await executarComBotaoDesabilitado(botao, async () => {
        try {
            await apiFetch(tipoEmEdicao ? `/api/plataforma/documentos/tipos/${tipoEmEdicao.tipo}` : '/api/plataforma/documentos/tipos', {
                method: tipoEmEdicao ? 'PUT' : 'POST', body: JSON.stringify(corpo)
            });
            modalTipo.hide();
            mostrarToast('Documento salvo no catálogo.');
            await carregarCatalogoAdmin();
        } catch (err) {
            mostrarErroFormulario('erro-tipo', err.message);
        }
    });
}

// ---------- Pacote ----------

function renderizarTiposDoPacote() {
    const grupos = {};
    catalogoAdmin.tipos.filter(t => t.tipo !== 99 && (t.ativo || tiposDoPacote.has(t.tipo))).forEach(t => {
        (grupos[t.categoriaNome] ??= []).push(t);
    });
    document.getElementById('pacote-tipos').innerHTML = Object.entries(grupos).map(([categoria, tipos]) => `
        <fieldset class="escolha-categoria">
            <legend>${escaparHtml(categoria)}</legend>
            <div class="escolha-grade">
                ${tipos.map(t => `<label class="escolha-item${tiposDoPacote.has(t.tipo) ? ' marcado' : ''}" for="pacote-tipo-${t.tipo}">
                    <input class="form-check-input" type="checkbox" id="pacote-tipo-${t.tipo}" data-tipo="${t.tipo}" ${tiposDoPacote.has(t.tipo) ? 'checked' : ''} />
                    <span class="escolha-item-texto"><span class="escolha-item-nome">${escaparHtml(t.nome)}</span>${t.ativo ? '' : '<small class="text-muted">inativo</small>'}</span>
                </label>`).join('')}
            </div>
        </fieldset>`).join('');
    document.getElementById('pacote-contagem').textContent = `(${tiposDoPacote.size} marcados)`;
}

function abrirPacote(pacote) {
    pacoteEmEdicao = pacote;
    ocultarErroFormulario('erro-pacote');
    tiposDoPacote = new Set(pacote?.tipos ?? []);
    document.getElementById('titulo-modal-pacote').textContent = pacote ? `Editar — ${pacote.nome}` : 'Novo pacote';
    document.getElementById('pacote-nome').value = pacote?.nome ?? '';
    document.getElementById('pacote-descricao').value = pacote?.descricao ?? '';
    document.getElementById('pacote-ordem').value = pacote?.ordem ?? 100;
    document.getElementById('pacote-ativo').checked = pacote?.ativo ?? true;
    renderizarTiposDoPacote();
    modalPacote.show();
}

async function salvarPacote(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-pacote');
    const corpo = {
        nome: document.getElementById('pacote-nome').value,
        descricao: document.getElementById('pacote-descricao').value,
        tipos: [...tiposDoPacote],
        ativo: document.getElementById('pacote-ativo').checked,
        ordem: Number(document.getElementById('pacote-ordem').value || 0)
    };
    const botao = e.submitter || e.target.querySelector('[type="submit"]');
    await executarComBotaoDesabilitado(botao, async () => {
        try {
            await apiFetch(pacoteEmEdicao ? `/api/plataforma/documentos/pacotes/${pacoteEmEdicao.id}` : '/api/plataforma/documentos/pacotes', {
                method: pacoteEmEdicao ? 'PUT' : 'POST', body: JSON.stringify(corpo)
            });
            modalPacote.hide();
            mostrarToast('Pacote salvo.');
            await carregarCatalogoAdmin();
        } catch (err) {
            mostrarErroFormulario('erro-pacote', err.message);
        }
    });
}

// ---------- Links por estado/município ----------

async function abrirLinks(tipo) {
    tipoDosLinks = tipo;
    ocultarErroFormulario('erro-links');
    document.getElementById('titulo-modal-links').textContent = `Links — ${tipo.nome}`;
    const municipal = tipo.abrangencia === 3;
    document.getElementById('grupo-link-uf').classList.toggle('d-none', municipal);
    document.getElementById('grupo-link-municipio').classList.toggle('d-none', !municipal);
    document.getElementById('link-local-url').value = '';
    $('#link-municipio').val(null).trigger('change');
    await carregarLinks();
    modalLinks.show();
}

async function carregarLinks() {
    let dados;
    try {
        dados = await apiFetch(`/api/plataforma/documentos/tipos/${tipoDosLinks.tipo}/links`);
    } catch (err) {
        mostrarErroFormulario('erro-links', err.message);
        return;
    }
    document.querySelector('#tabela-links-oficiais tbody').innerHTML = dados.oficiais.length === 0
        ? '<tr><td colspan="3" class="text-muted text-center py-3">Nenhum link oficial ainda.</td></tr>'
        : dados.oficiais.map(o => `<tr>
            <td>${escaparHtml(o.local)}</td>
            <td class="text-break"><a href="${escaparHtml(o.link)}" target="_blank" rel="noopener">${escaparHtml(o.link)}</a></td>
            <td class="text-end text-nowrap">
                <button type="button" class="btn btn-sm btn-outline-secondary" data-acao="editar-link" data-uf="${o.uf ?? ''}" data-municipio="${o.codigoMunicipio ?? ''}" data-local="${escaparHtml(o.local)}" data-link="${escaparHtml(o.link)}"><i class="bi bi-pencil"></i></button>
                <button type="button" class="btn btn-sm btn-outline-danger" data-acao="remover-link" data-uf="${o.uf ?? ''}" data-municipio="${o.codigoMunicipio ?? ''}" aria-label="Remover"><i class="bi bi-trash"></i></button>
            </td>
        </tr>`).join('');

    document.querySelector('#tabela-links-usados tbody').innerHTML = dados.usadosPelasEmpresas.length === 0
        ? '<tr><td colspan="5" class="text-muted text-center py-3">Nenhuma empresa cadastrou link próprio para este documento.</td></tr>'
        : dados.usadosPelasEmpresas.map(u => `<tr>
            <td>${escaparHtml(u.local)}</td>
            <td class="text-break"><a href="${escaparHtml(u.link)}" target="_blank" rel="noopener">${escaparHtml(u.link)}</a></td>
            <td class="text-end">${u.contas}</td>
            <td>${u.oficial ? '<span class="badge text-bg-success">Oficial</span>' : u.sugerido ? '<span class="badge text-bg-info">Sugerido</span>' : '<span class="badge text-bg-light border" title="Uma conta só e fora de .gov.br: não é sugerido para as outras">Só da conta</span>'}</td>
            <td class="text-end">${u.oficial ? '' : `<button type="button" class="btn btn-sm btn-outline-primary" data-acao="oficializar" data-uf="${u.uf ?? ''}" data-municipio="${u.codigoMunicipio ?? ''}" data-link="${escaparHtml(u.link)}">Oficializar</button>`}</td>
        </tr>`).join('');
}

async function salvarLinkLocal(uf, municipio, link, botao) {
    await executarComBotaoDesabilitado(botao, async () => {
        try {
            await apiFetch(`/api/plataforma/documentos/tipos/${tipoDosLinks.tipo}/links`, {
                method: 'PUT', body: JSON.stringify({ uf: uf || null, codigoMunicipio: municipio || null, link: link || null })
            });
            mostrarToast(link ? 'Link oficial salvo.' : 'Link oficial removido.');
            document.getElementById('link-local-url').value = '';
            await carregarLinks();
            await carregarCatalogoAdmin();
        } catch (err) {
            mostrarErroFormulario('erro-links', err.message);
        }
    });
}

document.addEventListener('DOMContentLoaded', function () {
    modalTipo = new bootstrap.Modal('#modal-tipo');
    modalPacote = new bootstrap.Modal('#modal-pacote');
    modalLinks = new bootstrap.Modal('#modal-links');

    document.getElementById('link-uf').innerHTML = UFS_BRASIL.map(uf => `<option value="${uf}">${uf}</option>`).join('');
    configurarSelectMunicipio({ select: 'link-municipio', modal: 'modal-links', placeholder: 'Digite o município' });

    document.getElementById('btn-novo-tipo').addEventListener('click', () => catalogoAdmin && abrirTipo(null));
    document.getElementById('btn-novo-pacote').addEventListener('click', () => catalogoAdmin && abrirPacote(null));
    document.getElementById('busca-tipos').addEventListener('input', () => catalogoAdmin && renderizarTipos());
    document.getElementById('tipo-abrangencia').addEventListener('change', atualizarCampoLink);
    document.getElementById('form-tipo').addEventListener('submit', salvarTipo);
    document.getElementById('form-pacote').addEventListener('submit', salvarPacote);

    document.getElementById('tabela-tipos').addEventListener('click', e => {
        const botao = e.target.closest('button[data-acao]');
        if (!botao) return;
        const tipo = catalogoAdmin.tipos.find(t => t.tipo === Number(botao.dataset.tipo));
        if (botao.dataset.acao === 'editar') abrirTipo(tipo);
        else if (botao.dataset.acao === 'links') abrirLinks(tipo);
    });
    document.getElementById('tabela-pacotes').addEventListener('click', e => {
        const botao = e.target.closest('button[data-acao="editar-pacote"]');
        if (botao) abrirPacote(catalogoAdmin.pacotes.find(p => p.id === botao.dataset.id));
    });
    document.getElementById('pacote-tipos').addEventListener('change', e => {
        const caixa = e.target.closest('input[data-tipo]');
        if (!caixa) return;
        const tipo = Number(caixa.dataset.tipo);
        if (caixa.checked) tiposDoPacote.add(tipo); else tiposDoPacote.delete(tipo);
        caixa.closest('.escolha-item').classList.toggle('marcado', caixa.checked);
        document.getElementById('pacote-contagem').textContent = `(${tiposDoPacote.size} marcados)`;
    });

    document.getElementById('form-link-local').addEventListener('submit', e => {
        e.preventDefault();
        ocultarErroFormulario('erro-links');
        const municipal = tipoDosLinks.abrangencia === 3;
        const link = document.getElementById('link-local-url').value.trim();
        if (!link) {
            mostrarErroFormulario('erro-links', 'Informe o link (para remover, use a lixeira na lista).');
            return;
        }
        salvarLinkLocal(municipal ? null : document.getElementById('link-uf').value,
            municipal ? $('#link-municipio').val() : null, link, e.submitter || e.target.querySelector('[type="submit"]'));
    });
    document.getElementById('modal-links').addEventListener('click', async e => {
        const botao = e.target.closest('button[data-acao]');
        if (!botao) return;
        const { acao, uf, municipio, link } = botao.dataset;
        if (acao === 'oficializar') {
            await salvarLinkLocal(uf, municipio, link, botao);
        } else if (acao === 'remover-link') {
            if (await confirmarAcao('Remover o link oficial deste lugar? As empresas voltam a ver a sugestão (se houver).', { titulo: 'Remover link', textoBotao: 'Remover', variante: 'perigo' }))
                await salvarLinkLocal(uf, municipio, '', botao);
        } else if (acao === 'editar-link') {
            if (municipio) {
                const opcao = new Option(botao.dataset.local, municipio, true, true);
                $('#link-municipio').append(opcao).trigger('change');
            } else {
                document.getElementById('link-uf').value = uf;
            }
            document.getElementById('link-local-url').value = link;
            document.getElementById('link-local-url').focus();
        }
    });

    carregarCatalogoAdmin();
});
