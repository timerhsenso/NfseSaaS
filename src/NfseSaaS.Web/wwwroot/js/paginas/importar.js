// IM1: importar clientes e contratos por planilha (Empresa selecionada no topo).
// Passo 1: escolher o arquivo. Passo 2: conferir linha a linha (nada é gravado).
// Passo 3: gravar. O arquivo fica só aqui no navegador entre os passos; ao gravar
// ele vai de novo e o servidor confere outra vez antes de gravar.
// Situação da linha (SituacaoLinhaImportacao): 1 nova, 2 atualiza, 3 já existe,
// 4 sem mudança, 5 erro.

(function () {
    const pagina = document.getElementById('importar-pagina');
    if (!pagina) return;

    const LIMITE_BYTES = 5 * 1024 * 1024;
    const EXTENSOES = ['.xlsx', '.csv', '.txt'];
    const SITUACAO = {
        1: { cor: 'success' },
        2: { cor: 'primary' },
        3: { cor: 'secondary' },
        4: { cor: 'secondary' },
        5: { cor: 'danger' }
    };
    const NOMES = {
        clientes: { titulo: 'Clientes', chave: 'CPF/CNPJ', descricao: 'Cliente', singular: 'cliente', plural: 'clientes' },
        contratos: { titulo: 'Contratos', chave: 'Nº do contrato', descricao: 'Contrato', singular: 'contrato', plural: 'contratos' }
    };

    const el = id => document.getElementById(id);
    const empresaId = obterEmpresaAtualId();

    let arquivo = null;
    let resultado = null;
    let abaAtual = null;
    let filtro = '';

    // ---------- Modelo ----------

    function configurarModelo() {
        const url = empresaId ? `/api/importacao/modelo?empresaId=${encodeURIComponent(empresaId)}` : '#';
        [el('btn-baixar-modelo'), el('link-baixar-modelo')].forEach(link => {
            if (!link) return;
            link.href = url;
            if (!empresaId) {
                link.classList.add('disabled');
                link.setAttribute('aria-disabled', 'true');
                link.title = 'Cadastre a sua empresa primeiro (menu Empresas).';
            }
        });
    }

    // ---------- Arquivo ----------

    function tamanhoLegivel(bytes) {
        if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1).replace('.', ',') + ' MB';
        return Math.max(1, Math.round(bytes / 1024)) + ' KB';
    }

    function mostrarErroArquivo(mensagem) {
        const caixa = el('importar-erro');
        caixa.textContent = mensagem || '';
        caixa.classList.toggle('d-none', !mensagem);
    }

    function escolherArquivo(novo) {
        mostrarErroArquivo('');
        if (!novo) return;
        const nome = novo.name.toLowerCase();
        if (nome.endsWith('.xls')) {
            mostrarErroArquivo('Planilha no formato antigo do Excel (.xls). No Excel, use Arquivo › Salvar como › Pasta de Trabalho do Excel (.xlsx).');
            return;
        }
        if (!EXTENSOES.some(ext => nome.endsWith(ext))) {
            mostrarErroArquivo('Envie uma planilha .xlsx (Excel) ou .csv.');
            return;
        }
        if (novo.size > LIMITE_BYTES) {
            mostrarErroArquivo('A planilha passa de 5 MB. Divida em arquivos menores.');
            return;
        }

        arquivo = novo;
        el('importar-zona').classList.add('escolhido');
        el('importar-zona-titulo').textContent = novo.name;
        el('importar-zona-dica').textContent = `${tamanhoLegivel(novo.size)} · clique para trocar`;
        el('btn-conferir').disabled = !empresaId;
        el('btn-conferir').focus();
    }

    function configurarZona() {
        const zona = el('importar-zona');
        const input = el('importar-arquivo');
        input.addEventListener('change', () => escolherArquivo(input.files[0]));

        ['dragenter', 'dragover'].forEach(evento => zona.addEventListener(evento, e => {
            e.preventDefault();
            zona.classList.add('arrastando');
        }));
        ['dragleave', 'dragend'].forEach(evento => zona.addEventListener(evento, () => zona.classList.remove('arrastando')));
        zona.addEventListener('drop', e => {
            e.preventDefault();
            zona.classList.remove('arrastando');
            const novo = e.dataTransfer?.files?.[0];
            if (novo) escolherArquivo(novo);
        });
    }

    function dadosDoEnvio() {
        const dados = new FormData();
        dados.append('empresaId', empresaId);
        dados.append('atualizarExistentes', el('importar-atualizar')?.checked ? 'true' : 'false');
        dados.append('arquivo', arquivo, arquivo.name);
        return dados;
    }

    // ---------- Conferir e gravar ----------

    async function conferir() {
        if (!arquivo || !empresaId) return;
        mostrarErroArquivo('');
        try {
            resultado = await enviarArquivoComProgresso('/api/importacao/conferir', dadosDoEnvio(), {
                botao: el('btn-conferir'),
                barra: el('importar-progresso')
            });
            marcarPasso(2);
            mostrarResultado();
        } catch (err) {
            mostrarErroArquivo(err.message);
        }
    }

    function contagemParaGravar(r) {
        return {
            clientesNovos: r.clientes.novas,
            clientesAtualizados: r.clientes.atualiza,
            contratos: r.contratos.novas
        };
    }

    function frase(n, singular, plural) {
        return `${n.toLocaleString('pt-BR')} ${n === 1 ? singular : plural}`;
    }

    function textoGravar(c) {
        const partes = [];
        if (c.clientesNovos) partes.push(frase(c.clientesNovos, 'cliente novo', 'clientes novos'));
        if (c.clientesAtualizados) partes.push(`${frase(c.clientesAtualizados, 'cliente', 'clientes')} para atualizar`);
        if (c.contratos) partes.push(frase(c.contratos, 'contrato', 'contratos'));
        if (partes.length === 0) return '';
        return partes.length === 1 ? partes[0] : partes.slice(0, -1).join(', ') + ' e ' + partes[partes.length - 1];
    }

    async function gravar() {
        if (!arquivo || !resultado || resultado.gravado) return;
        const contagem = contagemParaGravar(resultado);
        const texto = textoGravar(contagem);
        const erros = resultado.clientes.erros + resultado.contratos.erros;
        const ok = await confirmarAcao(
            `Gravar ${texto}?` + (erros ? ` As linhas com erro ficam de fora (${erros}).` : ''),
            { titulo: 'Gravar importação', textoBotao: 'Gravar' });
        if (!ok) return;

        try {
            resultado = await enviarArquivoComProgresso('/api/importacao/gravar', dadosDoEnvio(), {
                botao: el('btn-gravar'),
                barra: null
            });
            marcarPasso(3);
            mostrarResultado();
            el('importar-resultado').scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch (err) {
            mostrarErro(err.message);
        }
    }

    function recomecar() {
        arquivo = null;
        resultado = null;
        el('importar-arquivo').value = '';
        el('importar-zona').classList.remove('escolhido');
        el('importar-zona-titulo').textContent = 'Arraste a planilha aqui ou clique para escolher';
        el('importar-zona-dica').textContent = '.xlsx ou .csv, até 5 MB e 2.000 linhas';
        el('btn-conferir').disabled = true;
        el('importar-resultado').classList.add('d-none');
        el('importar-passo-arquivo').classList.remove('d-none');
        marcarPasso(1);
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    function marcarPasso(numero) {
        document.querySelectorAll('.importar-passo').forEach(p => {
            const n = Number(p.dataset.passo);
            p.classList.toggle('ativo', n === numero);
            p.classList.toggle('feito', n < numero);
        });
    }

    // ---------- Resultado ----------

    function mostrarResultado() {
        const r = resultado;
        el('importar-resultado').classList.remove('d-none');
        el('titulo-resultado').textContent = r.gravado ? 'Resultado da importação' : 'Confira antes de gravar';
        el('importar-arquivo-nome').textContent = r.gravado
            ? `Arquivo ${r.arquivo}. As linhas com erro não foram gravadas: baixe, corrija e envie de novo.`
            : `Arquivo ${r.arquivo}. Nada foi gravado ainda.`;

        // Gravado: o passo 1 some (o caminho de volta é "Outro arquivo").
        el('importar-passo-arquivo').classList.toggle('d-none', r.gravado);

        mostrarSucesso();
        mostrarAvisosGerais();
        mostrarResumo();

        const abas = r.abas.map(a => a.tipo);
        if (!abaAtual || !abas.includes(abaAtual)) {
            // Abre na aba com erro, se houver; senão a primeira.
            const comErro = r.abas.find(a => a.linhas.some(l => l.situacao === 5));
            abaAtual = (comErro || r.abas[0])?.tipo ?? null;
        }
        mostrarAbas();
        mostrarAba();
        mostrarRodape();
    }

    function mostrarSucesso() {
        const caixa = el('importar-sucesso');
        const r = resultado;
        if (!r.gravado) {
            caixa.classList.add('d-none');
            return;
        }
        const c = contagemParaGravar(r);
        const partes = [];
        if (c.clientesNovos) partes.push(frase(c.clientesNovos, 'cliente cadastrado', 'clientes cadastrados'));
        if (c.clientesAtualizados) partes.push(frase(c.clientesAtualizados, 'cliente atualizado', 'clientes atualizados'));
        if (c.contratos) partes.push(frase(c.contratos, 'contrato cadastrado', 'contratos cadastrados'));
        const links = [];
        if (r.abas.some(a => a.tipo === 'clientes')) links.push('<a href="/Clientes" class="alert-link">Ver clientes</a>');
        if (r.abas.some(a => a.tipo === 'contratos')) links.push('<a href="/Contratos" class="alert-link">Ver contratos</a>');
        caixa.innerHTML = `<i class="bi bi-check-circle-fill" aria-hidden="true"></i> `
            + (partes.length ? `<strong>Pronto!</strong> ${escaparHtml(partes.join(', '))}.` : 'Nada novo para gravar.')
            + (links.length ? ` ${links.join(' · ')}` : '');
        caixa.classList.remove('d-none');
    }

    function mostrarAvisosGerais() {
        const caixa = el('importar-avisos-gerais');
        caixa.innerHTML = (resultado.avisos || []).map(a =>
            `<div class="alert alert-warning py-2 small"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i> ${escaparHtml(a)}</div>`).join('');
    }

    function chip(numero, texto, cor) {
        if (!numero) return '';
        return `<span class="importar-chip importar-chip-${cor}"><b>${numero.toLocaleString('pt-BR')}</b> ${escaparHtml(texto)}</span>`;
    }

    function mostrarResumo() {
        const r = resultado;
        const gravado = r.gravado;
        const blocos = r.abas.map(a => {
            const t = a.tipo === 'clientes' ? r.clientes : r.contratos;
            const n = NOMES[a.tipo];
            const chips = [
                chip(t.novas, gravado ? (t.novas === 1 ? 'cadastrado' : 'cadastrados') : (t.novas === 1 ? 'novo' : 'novos'), 'sucesso'),
                chip(t.atualiza, gravado ? (t.atualiza === 1 ? 'atualizado' : 'atualizados') : (t.atualiza === 1 ? 'atualiza' : 'atualizam'), 'primario'),
                chip(t.jaExiste + t.semMudanca, 'já existem', 'neutro'),
                chip(t.erros, 'com erro', 'erro')
            ].join('');
            return `<div class="importar-resumo-bloco"><span class="importar-resumo-titulo">${escaparHtml(n.titulo)}</span>${chips || '<span class="small text-muted">nenhuma linha</span>'}</div>`;
        });
        el('importar-resumo').innerHTML = blocos.join('');
    }

    function mostrarAbas() {
        const lista = el('importar-abas');
        lista.classList.toggle('d-none', resultado.abas.length < 2);
        lista.innerHTML = resultado.abas.map(a => {
            const erros = a.linhas.filter(l => l.situacao === 5).length;
            const ativa = a.tipo === abaAtual;
            return `<li class="nav-item" role="presentation">
                <button type="button" class="nav-link${ativa ? ' active' : ''}" role="tab" aria-selected="${ativa}" data-aba="${a.tipo}">
                    ${escaparHtml(NOMES[a.tipo].titulo)} <span class="importar-contagem" title="Linhas">${a.linhas.length}</span>
                    ${erros ? `<span class="importar-contagem importar-contagem-erro" title="Linhas com erro">${erros}</span>` : ''}
                </button></li>`;
        }).join('');
    }

    function listaHtml(itens, classe, icone) {
        if (!itens || itens.length === 0) return '';
        return `<ul class="importar-obs ${classe}">${itens.map(i =>
            `<li><i class="bi ${icone}" aria-hidden="true"></i> ${escaparHtml(i)}</li>`).join('')}</ul>`;
    }

    function mostrarColunas(aba) {
        const partes = [];
        if (aba.colunasFaltando.length)
            partes.push(`<div class="text-danger"><i class="bi bi-x-octagon" aria-hidden="true"></i> Falta a coluna: <strong>${aba.colunasFaltando.map(escaparHtml).join(', ')}</strong></div>`);
        if (aba.colunasReconhecidas.length)
            partes.push(`<details><summary>Reconhecemos ${aba.colunasReconhecidas.length} ${aba.colunasReconhecidas.length === 1 ? 'coluna com outro nome' : 'colunas com outro nome'}</summary><span class="text-muted">${aba.colunasReconhecidas.map(escaparHtml).join(' · ')}</span></details>`);
        if (aba.colunasIgnoradas.length)
            partes.push(`<div class="text-muted"><i class="bi bi-eye-slash" aria-hidden="true"></i> Colunas ignoradas (não usamos): ${aba.colunasIgnoradas.map(escaparHtml).join(', ')}</div>`);
        el('importar-colunas').innerHTML = partes.join('');
    }

    function mostrarAba() {
        const aba = resultado.abas.find(a => a.tipo === abaAtual);
        const corpo = el('tabela-importacao').querySelector('tbody');
        if (!aba) {
            corpo.innerHTML = '';
            return;
        }

        const n = NOMES[aba.tipo];
        el('th-chave').textContent = n.chave;
        el('th-descricao').textContent = n.descricao;
        mostrarColunas(aba);

        const filtros = filtro ? filtro.split(',').map(Number) : null;
        const linhas = aba.linhas.filter(l => !filtros || filtros.includes(l.situacao));

        // Contagem em cada botão do filtro.
        document.querySelectorAll('#importar-filtros [data-filtro]').forEach(botao => {
            const valores = botao.dataset.filtro ? botao.dataset.filtro.split(',').map(Number) : null;
            const total = aba.linhas.filter(l => !valores || valores.includes(l.situacao)).length;
            botao.classList.toggle('active', botao.dataset.filtro === filtro);
            botao.dataset.total = total;
            botao.hidden = valores !== null && total === 0 && botao.dataset.filtro !== filtro;
        });

        corpo.innerHTML = linhas.map(l => {
            const s = SITUACAO[l.situacao] || SITUACAO[5];
            const obs = listaHtml(l.erros, 'importar-obs-erro', 'bi-x-circle')
                + listaHtml(l.mudancas, 'importar-obs-mudanca', 'bi-arrow-right-short')
                + listaHtml(l.avisos, 'importar-obs-aviso', 'bi-info-circle');
            return `<tr class="${l.situacao === 5 ? 'importar-linha-erro' : ''}">
                <td class="col-curta">${l.linha}</td>
                <td class="col-curta"><span class="badge text-bg-${s.cor}">${escaparHtml(l.situacaoTexto)}</span></td>
                <td class="col-codigo">${escaparHtml(l.chave || '—')}</td>
                <td>${escaparHtml(l.descricao || '')}</td>
                <td>${obs || '<span class="text-muted small">—</span>'}</td>
            </tr>`;
        }).join('');
        el('importar-vazio').classList.toggle('d-none', linhas.length > 0);
    }

    function mostrarRodape() {
        const r = resultado;
        const botao = el('btn-gravar');
        const erros = r.clientes.erros + r.contratos.erros;
        el('btn-baixar-erros').classList.toggle('d-none', erros === 0);

        if (r.gravado) {
            botao.classList.add('d-none');
            el('importar-rodape-texto').textContent = erros
                ? `${frase(erros, 'linha ficou', 'linhas ficaram')} de fora por erro.`
                : '';
            el('btn-trocar-arquivo').innerHTML = '<i class="bi bi-plus-lg" aria-hidden="true"></i> Importar outra planilha';
            return;
        }

        el('btn-trocar-arquivo').innerHTML = '<i class="bi bi-arrow-repeat" aria-hidden="true"></i> Outro arquivo';
        botao.classList.remove('d-none');
        const texto = textoGravar(contagemParaGravar(r));
        botao.disabled = texto === '';
        el('btn-gravar-texto').textContent = texto ? `Gravar ${texto}` : 'Nada para gravar';
        el('importar-rodape-texto').textContent = erros
            ? `${frase(erros, 'linha com erro fica', 'linhas com erro ficam')} de fora. Corrija na planilha e envie de novo, ou grave o resto agora.`
            : 'Tudo certo para gravar.';
    }

    async function baixarErros() {
        const abas = resultado.abas.map(a => ({
            nome: NOMES[a.tipo].titulo,
            cabecalhos: a.cabecalhos,
            linhas: a.linhas.filter(l => l.situacao === 5).map(l => ({ linha: l.linha, valores: l.valores, erro: l.erros.join(' | ') }))
        })).filter(a => a.linhas.length > 0);

        const botao = el('btn-baixar-erros');
        await executarComBotaoDesabilitado(botao, async () => {
            try {
                const resposta = await fetch('/api/importacao/relatorio-erros', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
                    body: JSON.stringify({ abas })
                });
                if (!resposta.ok) {
                    const corpo = await resposta.json().catch(() => null);
                    throw new Error(extrairMensagemDeErro(corpo) ?? `Erro ${resposta.status} ao gerar a planilha.`);
                }
                const blob = await resposta.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = 'linhas-com-erro.xlsx';
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                mostrarErro(err.message);
            }
        });
    }

    // ---------- Início ----------

    configurarModelo();
    configurarZona();
    el('btn-conferir').addEventListener('click', conferir);
    el('btn-gravar').addEventListener('click', gravar);
    el('btn-trocar-arquivo').addEventListener('click', recomecar);
    el('btn-baixar-erros').addEventListener('click', baixarErros);
    el('importar-atualizar')?.addEventListener('change', () => {
        // Mudou a opção depois de conferir: a conferência antiga não vale mais.
        if (resultado && !resultado.gravado) conferir();
    });
    el('importar-abas').addEventListener('click', e => {
        const botao = e.target.closest('[data-aba]');
        if (!botao) return;
        abaAtual = botao.dataset.aba;
        filtro = '';
        mostrarAbas();
        mostrarAba();
    });
    el('importar-filtros').addEventListener('click', e => {
        const botao = e.target.closest('[data-filtro]');
        if (!botao) return;
        filtro = botao.dataset.filtro;
        mostrarAba();
    });
})();
