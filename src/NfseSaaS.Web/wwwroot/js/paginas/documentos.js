// DOC1: tela "Documentos da empresa" (Empresa selecionada no topo).
// Cada envio é uma versão nova; a lista mostra a que vale de cada documento.
// Situação (SituacaoDocumentoEmpresa no servidor): 1 sem validade, 2 válido,
// 3 vence em breve (até 15 dias), 4 vencido.

const TIPO_OUTRO = 99;
const SITUACAO_DOCUMENTO = {
    1: { cor: 'secondary', icone: 'bi-file-earmark-check' },
    2: { cor: 'success', icone: 'bi-check-circle' },
    3: { cor: 'warning', icone: 'bi-exclamation-triangle' },
    4: { cor: 'danger', icone: 'bi-x-octagon' }
};

const paginaDocumentos = document.getElementById('documentos-pagina');
const podeEditarDocumentos = paginaDocumentos?.dataset.podeEditar === 'true';
let empresaDocumentosId = null;
let dadosDocumentos = null;

function dataBr(iso) {
    if (!iso) return '';
    const [ano, mes, dia] = String(iso).substring(0, 10).split('-');
    return `${dia}/${mes}/${ano}`;
}

function dataHoraBr(iso) {
    if (!iso) return '';
    return new Date(iso).toLocaleDateString('pt-BR');
}

function tamanhoLegivel(bytes) {
    if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1).replace('.', ',') + ' MB';
    return Math.max(1, Math.round(bytes / 1024)) + ' KB';
}

function badgeSituacao(versao) {
    if (!versao) return '<span class="badge text-bg-light border">Não enviado</span>';
    const s = SITUACAO_DOCUMENTO[versao.situacao] || SITUACAO_DOCUMENTO[1];
    const textoEscuro = s.cor === 'warning' ? ' text-dark' : '';
    return `<span class="badge text-bg-${s.cor}${textoEscuro}"><i class="bi ${s.icone}" aria-hidden="true"></i> ${escaparHtml(versao.situacaoTexto)}</span>`;
}

async function carregarDocumentos() {
    const lista = document.getElementById('lista-documentos');
    empresaDocumentosId = obterEmpresaAtualId();
    if (!empresaDocumentosId) {
        lista.innerHTML = '<div class="list-group-item text-center text-muted py-4">Selecione ou cadastre uma empresa.</div>';
        return;
    }

    try {
        dadosDocumentos = await apiFetch(`/api/documentos-empresa?empresaId=${encodeURIComponent(empresaDocumentosId)}`);
    } catch (err) {
        lista.innerHTML = `<div class="list-group-item text-center text-danger py-4">${escaparHtml(err.message)}</div>`;
        return;
    }

    const bloqueio = document.getElementById('documentos-bloqueio');
    bloqueio.classList.toggle('d-none', dadosDocumentos.envioLiberado);
    document.getElementById('documentos-bloqueio-texto').textContent = dadosDocumentos.mensagemBloqueio || '';

    const botaoNovo = document.getElementById('btn-novo-documento');
    if (botaoNovo) botaoNovo.classList.toggle('d-none', !dadosDocumentos.envioLiberado);
    document.getElementById('documentos-convite').classList.toggle('d-none', dadosDocumentos.jaEscolheu);

    renderizarResumo(dadosDocumentos.itens);
    renderizarLista(dadosDocumentos.itens);
}

function renderizarResumo(itens) {
    const conta = s => itens.filter(i => i.atual?.situacao === s).length;
    const naoEnviados = itens.filter(i => !i.atual).length;
    const chips = [
        { n: conta(4), texto: conta(4) === 1 ? 'vencido' : 'vencidos', cor: 'danger' },
        { n: conta(3), texto: 'vencendo em até 15 dias', cor: 'warning' },
        { n: conta(2) + conta(1), texto: 'em dia', cor: 'success' },
        { n: naoEnviados, texto: naoEnviados === 1 ? 'não enviado' : 'não enviados', cor: 'secondary' }
    ].filter(c => c.n > 0);

    document.getElementById('documentos-resumo').innerHTML = chips
        .map(c => `<span class="badge rounded-pill text-bg-${c.cor}${c.cor === 'warning' ? ' text-dark' : ''} documentos-chip">${c.n} ${c.texto}</span>`)
        .join('');
}

// DOC4: origem do link (OrigemLinkDocumento no servidor).
const ORIGEM_LINK = { empresa: 1, municipio: 2, estado: 3, sugerido: 4, catalogo: 5 };
const ABRANGENCIA_NACIONAL = 1;

function botoesLink(item, indice, liberado) {
    const link = item.link;
    const botoes = [];
    if (link) {
        botoes.push(`<a class="btn btn-sm btn-outline-secondary" href="${escaparHtml(link.link)}" target="_blank" rel="noopener"
            title="${escaparHtml(link.texto)} — ${escaparHtml(link.dominio)}" aria-label="Abrir o site oficial (${escaparHtml(link.dominio)})"><i class="bi bi-box-arrow-up-right"></i></a>`);
    }
    if (liberado && item.tipo !== TIPO_OUTRO) {
        const novo = !link && item.abrangencia !== ABRANGENCIA_NACIONAL;
        botoes.push(`<button type="button" class="btn btn-sm btn-outline-secondary" data-acao="link" data-indice="${indice}"
            title="${novo ? 'Cadastrar o link do site onde você tira este documento' : 'Trocar o link oficial'}" aria-label="${novo ? 'Cadastrar link' : 'Editar link'}">
            <i class="bi ${novo ? 'bi-link-45deg' : 'bi-pencil'}"></i>${novo ? ' Link' : ''}</button>`);
    }
    return botoes;
}

/** Linha de apoio do link: sugestão de outras empresas (com "Usar este link") ou de onde veio. */
function textoLink(item, indice, liberado) {
    const link = item.link;
    if (!link) return '';
    if (link.origem === ORIGEM_LINK.sugerido) {
        return `<div class="small documento-link-sugerido">
            <i class="bi bi-lightbulb" aria-hidden="true"></i> ${escaparHtml(link.texto)}: <span class="text-break">${escaparHtml(link.dominio)}</span>
            ${liberado ? `<button type="button" class="btn btn-link btn-sm p-0 align-baseline" data-acao="usar-link" data-indice="${indice}">Usar este link</button>` : ''}
        </div>`;
    }
    if (link.origem === ORIGEM_LINK.empresa)
        return `<div class="small text-muted"><i class="bi bi-link-45deg" aria-hidden="true"></i> Seu link: <span class="text-break">${escaparHtml(link.dominio)}</span></div>`;
    return '';
}

function renderizarLista(itens) {
    const lista = document.getElementById('lista-documentos');
    const liberado = dadosDocumentos.envioLiberado && podeEditarDocumentos;

    if (itens.length === 0) {
        lista.innerHTML = `<div class="list-group-item text-center text-muted py-4">Nenhum documento na lista desta empresa.${podeEditarDocumentos ? ' Use <strong>Escolher documentos</strong>.' : ''}</div>`;
        return;
    }

    let categoriaAtual = null;
    lista.innerHTML = itens.map((item, indice) => {
        const v = item.atual;
        const detalhes = v
            ? `<div class="small text-muted">
                   ${v.validaAte ? `Válido até <strong>${dataBr(v.validaAte)}</strong> · ` : ''}${v.dataEmissao ? `emitido em ${dataBr(v.dataEmissao)} · ` : ''}enviado em ${dataHoraBr(v.enviadoEm)}${v.enviadoPor ? ` por ${escaparHtml(v.enviadoPor)}` : ''}
               </div>
               ${v.observacao ? `<div class="small text-muted fst-italic">${escaparHtml(v.observacao)}</div>` : ''}`
            : (item.orgao && item.orgao !== '—' ? `<div class="small text-muted">${escaparHtml(item.orgao)}</div>` : '');

        const botoes = [];
        if (v) {
            botoes.push(`<a class="btn btn-sm btn-outline-secondary" href="/api/documentos-empresa/${v.id}/download" title="Baixar ${escaparHtml(v.nomeArquivo)}" aria-label="Baixar"><i class="bi bi-download"></i></a>`);
        }
        if (item.totalVersoes > 0) {
            botoes.push(`<button type="button" class="btn btn-sm btn-outline-secondary" data-acao="historico" data-indice="${indice}" title="Histórico">
                <i class="bi bi-clock-history"></i> ${item.totalVersoes}</button>`);
        }
        botoes.push(...botoesLink(item, indice, liberado));
        if (liberado) {
            botoes.push(`<button type="button" class="btn btn-sm ${v ? 'btn-outline-primary' : 'btn-primary'}" data-acao="enviar" data-indice="${indice}">
                <i class="bi bi-upload"></i> ${v ? 'Nova versão' : 'Enviar'}</button>`);
        }

        // Cabeçalho da categoria (Fiscal, Trabalhista...) quando muda.
        let cabecalho = '';
        const categoria = item.tipo === TIPO_OUTRO ? 'Outros documentos' : item.categoriaNome;
        if (categoria !== categoriaAtual) {
            categoriaAtual = categoria;
            cabecalho = `<div class="list-group-item documentos-categoria">${escaparHtml(categoria)}</div>`;
        }

        return `${cabecalho}<div class="list-group-item documento-linha${v ? '' : ' documento-pendente'}">
            <div class="documento-info">
                <div class="d-flex flex-wrap align-items-center gap-2">
                    <span class="fw-semibold">${escaparHtml(item.titulo)}</span>
                    ${badgeSituacao(v)}
                </div>
                ${detalhes}
                ${textoLink(item, indice, liberado)}
            </div>
            <div class="documento-acoes">${botoes.join('')}</div>
        </div>`;
    }).join('');
}

// ---------- Link oficial (DOC4) ----------

let itemLinkAtual = null;

function abrirLink(item) {
    itemLinkAtual = item;
    ocultarErroFormulario('erro-link-documento');
    document.getElementById('titulo-link-documento').textContent = `Link oficial — ${item.titulo}`;
    const campo = document.getElementById('link-documento-url');
    campo.value = item.linkProprio || '';
    const atual = item.link;
    document.getElementById('link-documento-atual').textContent = atual
        ? (atual.origem === ORIGEM_LINK.empresa ? `Hoje vale o link que você cadastrou (${atual.dominio}).` : `Hoje vale: ${atual.texto} (${atual.dominio}).`)
        : 'Este documento ainda não tem link. Cadastre o site da prefeitura, da SEFAZ ou do órgão que emite.';
    document.getElementById('btn-remover-link-documento').classList.toggle('d-none', !item.linkProprio);
    bootstrap.Modal.getOrCreateInstance(document.getElementById('modal-link-documento')).show();
    setTimeout(() => campo.focus(), 300);
}

async function salvarLink(item, link, botao) {
    await executarComBotaoDesabilitado(botao, async () => {
        try {
            await apiFetch('/api/documentos-empresa/link', {
                method: 'PUT',
                body: JSON.stringify({ empresaId: empresaDocumentosId, tipo: item.tipo, link: link || null })
            });
            bootstrap.Modal.getInstance(document.getElementById('modal-link-documento'))?.hide();
            mostrarToast(link ? 'Link salvo para esta empresa.' : 'Seu link foi removido.');
            await carregarDocumentos();
        } catch (err) {
            if (document.getElementById('modal-link-documento').classList.contains('show'))
                mostrarErroFormulario('erro-link-documento', err.message);
            else
                mostrarErro(err.message);
        }
    });
}

// ---------- Escolher documentos (DOC4) ----------

let escolha = null;
let marcadosEscolha = new Set();

async function abrirEscolha() {
    if (!empresaDocumentosId) return;
    ocultarErroFormulario('erro-escolha');
    try {
        escolha = await apiFetch(`/api/documentos-empresa/escolha?empresaId=${encodeURIComponent(empresaDocumentosId)}`);
    } catch (err) {
        mostrarErro(err.message);
        return;
    }
    marcadosEscolha = new Set(escolha.categorias.flatMap(c => c.tipos.filter(t => t.marcado).map(t => t.tipo)));
    document.getElementById('escolha-busca').value = '';
    const copiar = document.getElementById('escolha-copiar');
    copiar.checked = false;
    document.getElementById('escolha-copiar-bloco').classList.toggle('d-none', escolha.outrasEmpresas === 0);
    document.getElementById('escolha-copiar-texto').textContent = escolha.outrasEmpresas === 1
        ? 'Usar a mesma lista na outra empresa da conta'
        : `Usar a mesma lista nas outras ${escolha.outrasEmpresas} empresas da conta`;
    renderizarPacotes();
    renderizarEscolha();
    bootstrap.Modal.getOrCreateInstance(document.getElementById('modal-escolher-documentos')).show();
}

function nomesDoCatalogo() {
    const nomes = {};
    escolha.categorias.forEach(c => c.tipos.forEach(t => { nomes[t.tipo] = t.nome; }));
    return nomes;
}

function renderizarPacotes() {
    const bloco = document.getElementById('escolha-pacotes-bloco');
    bloco.classList.toggle('d-none', escolha.pacotes.length === 0);
    const nomes = nomesDoCatalogo();
    document.getElementById('escolha-pacotes').innerHTML = escolha.pacotes.map((p, i) => {
        const completo = p.tipos.every(t => marcadosEscolha.has(t));
        return `<button type="button" class="escolha-pacote${completo ? ' aplicado' : ''}" data-pacote="${i}" aria-pressed="${completo}"
                title="${escaparHtml(p.tipos.map(t => nomes[t]).filter(Boolean).join(', '))}">
            <i class="bi ${completo ? 'bi-check-circle-fill' : 'bi-plus-circle'}" aria-hidden="true"></i>
            <span><strong>${escaparHtml(p.nome)}</strong>${p.descricao ? `<small>${escaparHtml(p.descricao)}</small>` : ''}</span>
            <span class="escolha-pacote-total">${p.tipos.length}</span>
        </button>`;
    }).join('');
}

function semAcento(texto) {
    return (texto || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

const TEXTO_ABRANGENCIA = { 1: '', 2: 'link por estado', 3: 'link por município' };

function renderizarEscolha() {
    const busca = semAcento(document.getElementById('escolha-busca').value.trim());
    let visiveis = 0;
    const html = escolha.categorias.map(c => {
        const tipos = c.tipos.filter(t => !busca || semAcento(`${t.nome} ${t.orgao}`).includes(busca));
        if (tipos.length === 0) return '';
        visiveis += tipos.length;
        const marcadosCategoria = c.tipos.filter(t => marcadosEscolha.has(t.tipo)).length;
        return `<fieldset class="escolha-categoria">
            <legend><span>${escaparHtml(c.nome)}</span> <small class="text-muted">${marcadosCategoria} de ${c.tipos.length}</small></legend>
            <div class="escolha-grade">
                ${tipos.map(t => {
                    const id = `escolha-tipo-${t.tipo}`;
                    const extras = [];
                    if (t.validadeDias) extras.push(`vale ${t.validadeDias} dias`);
                    if (TEXTO_ABRANGENCIA[t.abrangencia]) extras.push(TEXTO_ABRANGENCIA[t.abrangencia]);
                    const selos = [];
                    if (t.temArquivo) selos.push('<span class="badge text-bg-light border"><i class="bi bi-file-earmark-pdf" aria-hidden="true"></i> tem arquivo</span>');
                    if (t.clientesExigem > 0) selos.push(`<span class="badge text-bg-light border"><i class="bi bi-people" aria-hidden="true"></i> ${t.clientesExigem} ${t.clientesExigem === 1 ? 'cliente exige' : 'clientes exigem'}</span>`);
                    if (t.inativo) selos.push('<span class="badge text-bg-warning text-dark">saiu do catálogo</span>');
                    return `<label class="escolha-item${marcadosEscolha.has(t.tipo) ? ' marcado' : ''}" for="${id}">
                        <input class="form-check-input" type="checkbox" id="${id}" data-tipo="${t.tipo}" ${marcadosEscolha.has(t.tipo) ? 'checked' : ''} />
                        <span class="escolha-item-texto">
                            <span class="escolha-item-nome">${escaparHtml(t.nome)}</span>
                            <small class="text-muted">${escaparHtml([t.orgao !== '—' ? t.orgao : null, ...extras].filter(Boolean).join(' · '))}</small>
                            ${selos.length ? `<span class="escolha-item-selos">${selos.join(' ')}</span>` : ''}
                        </span>
                    </label>`;
                }).join('')}
            </div>
        </fieldset>`;
    }).join('');
    document.getElementById('escolha-categorias').innerHTML = html;
    document.getElementById('escolha-vazio').classList.toggle('d-none', visiveis > 0);
    atualizarContagemEscolha();
}

function atualizarContagemEscolha() {
    const n = marcadosEscolha.size;
    document.getElementById('escolha-contagem').textContent = String(n);
    document.getElementById('escolha-contagem-texto').textContent = n === 1 ? 'documento marcado' : 'documentos marcados';
}

async function salvarEscolha() {
    const botao = document.getElementById('btn-salvar-escolha');
    ocultarErroFormulario('erro-escolha');

    // Desmarcar não apaga nada, mas some da lista: avisa o que tem arquivo ou é exigido por cliente.
    const retirados = escolha.categorias.flatMap(c => c.tipos).filter(t => t.marcado && !marcadosEscolha.has(t.tipo));
    const sensiveis = retirados.filter(t => t.temArquivo || t.clientesExigem > 0);
    if (sensiveis.length > 0) {
        const detalhes = sensiveis.map(t => {
            const motivos = [];
            if (t.temArquivo) motivos.push('tem arquivo guardado');
            if (t.clientesExigem > 0) motivos.push(`${t.clientesExigem} ${t.clientesExigem === 1 ? 'cliente exige' : 'clientes exigem'} na nota`);
            return `${t.nome} (${motivos.join(', ')})`;
        }).join('; ');
        const ok = await confirmarAcao(
            `Vai sair da lista: ${detalhes}. Nada é apagado: os arquivos continuam guardados e voltam se você marcar de novo. Os clientes que exigem passam a receber o aviso de "faltando".`,
            { titulo: 'Tirar da lista?', textoBotao: 'Tirar assim mesmo' });
        if (!ok) return;
    }

    const copiar = document.getElementById('escolha-copiar').checked;
    await executarComBotaoDesabilitado(botao, async () => {
        try {
            await apiFetch('/api/documentos-empresa/escolha', {
                method: 'PUT',
                body: JSON.stringify({ empresaId: empresaDocumentosId, tipos: [...marcadosEscolha] })
            });
            let mensagem = 'Lista de documentos salva.';
            if (copiar) {
                const r = await apiFetch('/api/documentos-empresa/escolha/copiar', {
                    method: 'POST', body: JSON.stringify({ empresaOrigemId: empresaDocumentosId })
                });
                mensagem = `Lista salva aqui e em mais ${r.empresas} ${r.empresas === 1 ? 'empresa' : 'empresas'}.`;
            }
            bootstrap.Modal.getInstance(document.getElementById('modal-escolher-documentos'))?.hide();
            mostrarToast(mensagem);
            await carregarDocumentos();
        } catch (err) {
            mostrarErroFormulario('erro-escolha', err.message);
        }
    });
}

// ---------- Envio ----------

function preencherTipos() {
    const select = document.getElementById('documento-tipo');
    if (!select || select.options.length) return;
    select.innerHTML = dadosDocumentos.tipos
        .map(t => `<option value="${t.tipo}">${escaparHtml(t.nome)}</option>`)
        .join('');
}

function tipoInfo(tipo) {
    return dadosDocumentos.tipos.find(t => t.tipo === Number(tipo));
}

function atualizarCamposTipo() {
    const info = tipoInfo(document.getElementById('documento-tipo').value);
    document.getElementById('grupo-documento-nome').classList.toggle('d-none', info.tipo !== TIPO_OUTRO);

    const link = document.getElementById('documento-link');
    link.classList.toggle('d-none', !info.linkEmissao);
    if (info.linkEmissao) {
        const a = document.getElementById('documento-link-a');
        a.href = info.linkEmissao;
        a.textContent = `Tirar no site oficial (${info.orgao})`;
    }

    const dica = document.getElementById('documento-dica-validade');
    if (info.validadeSugeridaDias) {
        dica.textContent = `Costuma valer ${info.validadeSugeridaDias} dias: já preenchemos, confira no documento.`;
    } else if (info.exigeValidade) {
        dica.textContent = 'Certidão sempre vence: informe os dias ou a data que vêm escritos nela.';
    } else {
        dica.textContent = 'Informe os dias ou a data final. Deixe os dois em branco se o documento não vence.';
    }
    // Tipo com prazo usual: sugere os dias se a pessoa ainda não informou validade.
    const dias = document.getElementById('documento-dias');
    const validade = document.getElementById('documento-validade');
    if (info.validadeSugeridaDias && !dias.value && !validade.value)
        dias.value = String(info.validadeSugeridaDias);
    sincronizarValidade('dias');
}

// ---- Validade: "vale por N dias" e "válido até" andam juntos, a partir da emissão ----
const VALIDADE_MAXIMA_DIAS = 3650;

function dataDeIsoDocumento(iso) {
    const [ano, mes, dia] = iso.split('-').map(Number);
    return new Date(ano, mes - 1, dia);
}

function diasEntre(inicioIso, fimIso) {
    return Math.round((dataDeIsoDocumento(fimIso) - dataDeIsoDocumento(inicioIso)) / 86_400_000);
}

/** origem: 'dias' (vale a contagem de dias) ou 'data' (vale a data final). */
function sincronizarValidade(origem) {
    const emissao = document.getElementById('documento-emissao').value;
    const dias = document.getElementById('documento-dias');
    const validade = document.getElementById('documento-validade');

    if (emissao) {
        const n = parseInt(dias.value, 10);
        if (origem === 'dias') {
            if (n >= 1 && n <= VALIDADE_MAXIMA_DIAS) {
                const fim = dataDeIsoDocumento(emissao);
                fim.setDate(fim.getDate() + n);
                validade.value = dataLocalIso(fim);
            } else if (!dias.value) {
                validade.value = '';
            }
        } else if (validade.value) {
            const calculado = diasEntre(emissao, validade.value);
            dias.value = calculado >= 1 ? String(calculado) : '';
        } else {
            dias.value = '';
        }
    }
    atualizarResumoValidade();
}

function atualizarResumoValidade() {
    const emissao = document.getElementById('documento-emissao').value;
    const dias = document.getElementById('documento-dias').value;
    const validade = document.getElementById('documento-validade').value;
    const resumo = document.getElementById('documento-resumo-validade');
    const info = tipoInfo(document.getElementById('documento-tipo').value);
    resumo.classList.remove('text-danger', 'text-success');

    if (!validade) {
        resumo.textContent = info?.exigeValidade ? '' : (dias ? '' : 'Sem validade: o documento não vence.');
        return;
    }
    const hoje = dataLocalIso();
    const fim = dataDeIsoDocumento(validade).toLocaleDateString('pt-BR');
    const restantes = diasEntre(hoje, validade);
    const prazo = emissao && dias ? `Vale ${dias} ${Number(dias) === 1 ? 'dia' : 'dias'}, até ${fim}` : `Vale até ${fim}`;
    if (restantes < 0) {
        resumo.textContent = `${prazo}: já venceu há ${-restantes} ${restantes === -1 ? 'dia' : 'dias'}.`;
        resumo.classList.add('text-danger');
    } else {
        resumo.textContent = `${prazo}: ${restantes === 0 ? 'vence hoje' : restantes === 1 ? 'vence amanhã' : `faltam ${restantes} dias`}.`;
        resumo.classList.add('text-success');
    }
}

/** Nomes já usados em "Outro documento" nesta empresa: sugeridos ao digitar, para não criar dois iguais. */
function preencherNomesUsados() {
    const nomes = [...new Set((dadosDocumentos?.itens ?? [])
        .map(i => i.nomePersonalizado)
        .filter(Boolean))].sort((a, b) => a.localeCompare(b, 'pt-BR'));
    document.getElementById('documento-nomes-usados').innerHTML = nomes.map(n => `<option value="${escaparHtml(n)}"></option>`).join('');
}

function limparAvisoVencido() {
    document.getElementById('aviso-documento-vencido').classList.add('d-none');
    const botao = document.getElementById('btn-salvar-documento');
    delete botao.dataset.confirmouVencido;
    botao.textContent = 'Enviar';
}

function abrirEnvio(item) {
    preencherTipos();
    const form = document.getElementById('form-documento');
    form.reset();
    ocultarErroFormulario('erro-documento');
    document.getElementById('documento-dias').value = '';
    document.getElementById('documento-progresso').hidden = true;
    limparAvisoVencido();
    preencherNomesUsados();

    const select = document.getElementById('documento-tipo');
    select.value = String(item ? item.tipo : TIPO_OUTRO);
    // Nova versão: o documento já está escolhido (não muda de "lugar").
    select.disabled = !!item;
    const nome = document.getElementById('documento-nome');
    nome.value = item?.nomePersonalizado || '';
    nome.readOnly = !!item?.nomePersonalizado;

    document.getElementById('titulo-modal-documento').textContent = item
        ? (item.atual ? `Nova versão — ${item.titulo}` : `Enviar — ${item.titulo}`)
        : 'Outro documento';
    document.getElementById('documento-emissao').max = dataLocalIso();
    atualizarCamposTipo();
    bootstrap.Modal.getOrCreateInstance(document.getElementById('modal-documento')).show();
}

async function salvarDocumento(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-documento');

    const arquivo = document.getElementById('documento-arquivo').files[0];
    if (!arquivo) {
        mostrarErroFormulario('erro-documento', 'Escolha o arquivo do documento (PDF).');
        return;
    }
    if (arquivo.size > 5 * 1024 * 1024) {
        mostrarErroFormulario('erro-documento', `O arquivo tem ${(arquivo.size / 1024 / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} MB e o limite é 5 MB.`);
        return;
    }
    const campoEmissao = document.getElementById('documento-emissao');
    if (!campoEmissao.value) {
        mostrarErroFormulario('erro-documento', 'Informe a data de emissão do documento (vem escrita nele).');
        campoEmissao.focus();
        return;
    }
    const campoDias = document.getElementById('documento-dias');
    if (campoDias.value && !(parseInt(campoDias.value, 10) >= 1 && parseInt(campoDias.value, 10) <= VALIDADE_MAXIMA_DIAS)) {
        mostrarErroFormulario('erro-documento', `Os dias de validade vão de 1 a ${VALIDADE_MAXIMA_DIAS}.`);
        campoDias.focus();
        return;
    }

    const info = tipoInfo(document.getElementById('documento-tipo').value);
    if (info?.exigeValidade && !document.getElementById('documento-validade').value) {
        mostrarErroFormulario('erro-documento', 'Certidão sempre vence: informe os dias ou a data de validade que vêm escritos nela.');
        campoDias.focus();
        return;
    }

    // Vencido pela data informada: avisa dentro do próprio modal e pede o
    // segundo clique (sem abrir outra janela por cima desta).
    const validade = document.getElementById('documento-validade').value;
    const botaoSalvar = document.getElementById('btn-salvar-documento');
    const avisoVencido = document.getElementById('aviso-documento-vencido');
    if (validade && validade < dataLocalIso() && botaoSalvar.dataset.confirmouVencido !== validade) {
        avisoVencido.classList.remove('d-none');
        botaoSalvar.dataset.confirmouVencido = validade;
        botaoSalvar.textContent = 'Guardar assim mesmo';
        return;
    }

    const dados = new FormData();
    dados.append('empresaId', empresaDocumentosId);
    dados.append('tipo', document.getElementById('documento-tipo').value);
    dados.append('nomePersonalizado', document.getElementById('documento-nome').value);
    dados.append('dataEmissao', document.getElementById('documento-emissao').value);
    dados.append('validaAte', validade);
    dados.append('observacao', document.getElementById('documento-observacao').value);
    dados.append('arquivo', arquivo);

    if (botaoSalvar.disabled) return;
    try {
        // Multipart com progresso (botão "Enviando 45%" e barra).
        await enviarArquivoComProgresso('/api/documentos-empresa', dados, {
            botao: botaoSalvar, barra: document.getElementById('documento-progresso')
        });
        bootstrap.Modal.getOrCreateInstance(document.getElementById('modal-documento')).hide();
        mostrarToast('Documento guardado.');
        await carregarDocumentos();
    } catch (err) {
        mostrarErroFormulario('erro-documento', err.message);
    }
}

// ---------- Histórico ----------

async function abrirHistorico(item) {
    document.getElementById('titulo-historico-documento').textContent = `Histórico — ${item.titulo}`;
    const lista = document.getElementById('lista-historico-documento');
    lista.innerHTML = '<div class="list-group-item text-center text-muted py-4">Carregando...</div>';
    bootstrap.Modal.getOrCreateInstance(document.getElementById('modal-historico-documento')).show();
    await renderizarHistorico(item);
}

async function renderizarHistorico(item) {
    const lista = document.getElementById('lista-historico-documento');
    let versoes;
    try {
        const nome = item.nomePersonalizado ? `&nome=${encodeURIComponent(item.nomePersonalizado)}` : '';
        versoes = await apiFetch(`/api/documentos-empresa/historico?empresaId=${encodeURIComponent(empresaDocumentosId)}&tipo=${item.tipo}${nome}`);
    } catch (err) {
        lista.innerHTML = `<div class="list-group-item text-danger">${escaparHtml(err.message)}</div>`;
        return;
    }
    if (!versoes.length) {
        lista.innerHTML = '<div class="list-group-item text-center text-muted py-4">Nenhuma versão.</div>';
        return;
    }

    lista.innerHTML = versoes.map((v, i) => `
        <div class="list-group-item documento-linha">
            <div class="documento-info">
                <div class="d-flex flex-wrap align-items-center gap-2">
                    <span class="fw-semibold">${escaparHtml(v.nomeArquivo)}</span>
                    ${i === 0 ? '<span class="badge text-bg-primary">Vale esta</span>' : ''}
                    ${badgeSituacao(v)}
                </div>
                <div class="small text-muted">
                    ${v.validaAte ? `Válido até ${dataBr(v.validaAte)} · ` : ''}${v.dataEmissao ? `emitido em ${dataBr(v.dataEmissao)} · ` : ''}${tamanhoLegivel(v.tamanhoBytes)} · enviado em ${dataHoraBr(v.enviadoEm)}${v.enviadoPor ? ` por ${escaparHtml(v.enviadoPor)}` : ''}
                </div>
                ${v.observacao ? `<div class="small text-muted fst-italic">${escaparHtml(v.observacao)}</div>` : ''}
            </div>
            <div class="documento-acoes">
                <a class="btn btn-sm btn-outline-secondary" href="/api/documentos-empresa/${v.id}/download" aria-label="Baixar"><i class="bi bi-download"></i></a>
                ${podeEditarDocumentos ? `<button type="button" class="btn btn-sm btn-outline-danger" data-excluir-versao="${v.id}" aria-label="Excluir esta versão"><i class="bi bi-trash"></i></button>` : ''}
            </div>
        </div>`).join('');

    // Excluir com confirmação na própria linha (sem abrir outra janela por cima do histórico).
    lista.querySelectorAll('[data-excluir-versao]').forEach(botao => {
        botao.onclick = () => {
            const acoes = botao.closest('.documento-acoes');
            acoes.innerHTML = `<span class="small text-danger me-1">Excluir esta versão?</span>
                <button type="button" class="btn btn-sm btn-danger" data-confirmar>Excluir</button>
                <button type="button" class="btn btn-sm btn-outline-secondary" data-cancelar>Não</button>`;
            acoes.querySelector('[data-cancelar]').onclick = () => renderizarHistorico(item);
            const confirmar = acoes.querySelector('[data-confirmar]');
            confirmar.onclick = () => executarComBotaoDesabilitado(confirmar, async () => {
                try {
                    await apiFetch(`/api/documentos-empresa/${botao.dataset.excluirVersao}`, { method: 'DELETE' });
                    mostrarToast('Versão excluída.');
                    await carregarDocumentos();
                    const atualizado = dadosDocumentos.itens.find(x => x.tipo === item.tipo && (x.nomePersonalizado || '') === (item.nomePersonalizado || ''));
                    if (atualizado && atualizado.totalVersoes > 0) await renderizarHistorico(atualizado);
                    else bootstrap.Modal.getOrCreateInstance(document.getElementById('modal-historico-documento')).hide();
                } catch (err) {
                    mostrarErro(err.message);
                }
            });
        };
    });
}

// ---------- Eventos ----------

document.getElementById('lista-documentos').addEventListener('click', e => {
    const botao = e.target.closest('button[data-acao]');
    if (!botao || !dadosDocumentos) return;
    const item = dadosDocumentos.itens[Number(botao.dataset.indice)];
    if (botao.dataset.acao === 'enviar') abrirEnvio(item);
    else if (botao.dataset.acao === 'historico') abrirHistorico(item);
    else if (botao.dataset.acao === 'link') abrirLink(item);
    else if (botao.dataset.acao === 'usar-link') salvarLink(item, item.link.link, botao);
});

if (podeEditarDocumentos) {
    document.getElementById('btn-novo-documento').addEventListener('click', () => dadosDocumentos && abrirEnvio(null));

    // DOC4: escolher documentos e link oficial.
    document.getElementById('btn-escolher-documentos').addEventListener('click', abrirEscolha);
    document.querySelectorAll('[data-abrir-escolha]').forEach(b => b.addEventListener('click', abrirEscolha));
    document.getElementById('escolha-busca').addEventListener('input', renderizarEscolha);
    document.getElementById('escolha-categorias').addEventListener('change', e => {
        const caixa = e.target.closest('input[data-tipo]');
        if (!caixa) return;
        const tipo = Number(caixa.dataset.tipo);
        if (caixa.checked) marcadosEscolha.add(tipo); else marcadosEscolha.delete(tipo);
        caixa.closest('.escolha-item').classList.toggle('marcado', caixa.checked);
        renderizarPacotes();
        // Atualiza o "N de M" da categoria sem redesenhar a lista (mantém o foco).
        const fieldset = caixa.closest('fieldset');
        const categoria = escolha.categorias.find(c => c.tipos.some(t => t.tipo === tipo));
        fieldset.querySelector('legend small').textContent = `${categoria.tipos.filter(t => marcadosEscolha.has(t.tipo)).length} de ${categoria.tipos.length}`;
        atualizarContagemEscolha();
    });
    document.getElementById('escolha-pacotes').addEventListener('click', e => {
        const botao = e.target.closest('button[data-pacote]');
        if (!botao) return;
        const pacote = escolha.pacotes[Number(botao.dataset.pacote)];
        const completo = pacote.tipos.every(t => marcadosEscolha.has(t));
        if (!completo) {
            pacote.tipos.forEach(t => marcadosEscolha.add(t));
        } else {
            // Desaplicar tira só o que é do pacote: os básicos (padrão) e o que outro
            // pacote aplicado também traz continuam marcados.
            const padrao = new Set(escolha.categorias.flatMap(c => c.tipos.filter(t => t.padrao).map(t => t.tipo)));
            const outrosAplicados = escolha.pacotes.filter(p => p !== pacote && p.tipos.every(t => marcadosEscolha.has(t)));
            pacote.tipos
                .filter(t => !padrao.has(t) && !outrosAplicados.some(p => p.tipos.includes(t)))
                .forEach(t => marcadosEscolha.delete(t));
        }
        renderizarPacotes();
        renderizarEscolha();
    });
    document.getElementById('btn-salvar-escolha').addEventListener('click', salvarEscolha);

    document.getElementById('form-link-documento').addEventListener('submit', e => {
        e.preventDefault();
        if (!itemLinkAtual) return;
        const valor = document.getElementById('link-documento-url').value.trim();
        if (valor && !/^https:\/\//i.test(valor) && /^[a-z]+:\/\//i.test(valor)) {
            mostrarErroFormulario('erro-link-documento', 'Use um link que comece com https://');
            return;
        }
        salvarLink(itemLinkAtual, valor, document.getElementById('btn-salvar-link-documento'));
    });
    document.getElementById('btn-remover-link-documento').addEventListener('click', function () {
        if (itemLinkAtual) salvarLink(itemLinkAtual, '', this);
    });
    document.getElementById('form-documento').addEventListener('submit', salvarDocumento);
    document.getElementById('documento-tipo').addEventListener('change', () => {
        // Outro tipo: recomeça a validade (o prazo usual muda).
        document.getElementById('documento-dias').value = '';
        document.getElementById('documento-validade').value = '';
        atualizarCamposTipo();
    });
    // Emissão mudou: os dias informados valem; sem dias, recalcula pela data final.
    document.getElementById('documento-emissao').addEventListener('change', () => {
        sincronizarValidade(document.getElementById('documento-dias').value ? 'dias' : 'data');
        limparAvisoVencido();
    });
    document.getElementById('documento-dias').addEventListener('input', () => { sincronizarValidade('dias'); limparAvisoVencido(); });
    document.getElementById('documento-validade').addEventListener('input', () => { sincronizarValidade('data'); limparAvisoVencido(); });
}

carregarDocumentos();
