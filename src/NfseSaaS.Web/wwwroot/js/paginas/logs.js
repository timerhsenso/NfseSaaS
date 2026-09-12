document.addEventListener('DOMContentLoaded', async function () {
    document.getElementById('form-filtros-logs').addEventListener('submit', function (e) {
        e.preventDefault();
        carregarConteudoLog();
    });

    await carregarArquivosLog();
    await carregarConteudoLog();
});

async function carregarArquivosLog() {
    const select = document.getElementById('filtro-arquivo-log');

    try {
        const arquivos = await apiFetch('/api/logs/arquivos');

        if (!arquivos.length) {
            select.innerHTML = '<option value="">Nenhum arquivo de log encontrado</option>';
            return;
        }

        select.innerHTML = arquivos.map(function (arquivo, indice) {
            const tamanhoKb = Math.round(arquivo.tamanhoBytes / 1024);
            const selecionado = indice === 0 ? 'selected' : '';
            return `<option value="${arquivo.nome}" ${selecionado}>${arquivo.nome} (${tamanhoKb} KB)</option>`;
        }).join('');
    } catch (erro) {
        select.innerHTML = '<option value="">Erro ao listar arquivos</option>';
    }
}

async function carregarConteudoLog() {
    const arquivo = document.getElementById('filtro-arquivo-log').value;
    const preConteudo = document.getElementById('conteudo-log');
    const avisoTruncado = document.getElementById('aviso-truncado-log');
    const avisoVazio = document.getElementById('aviso-vazio-log');

    avisoTruncado.classList.add('d-none');
    avisoVazio.classList.add('d-none');

    if (!arquivo) {
        preConteudo.textContent = '';
        return;
    }

    const parametros = new URLSearchParams({
        arquivo: arquivo,
        linhas: document.getElementById('filtro-linhas-log').value
    });

    const nivel = document.getElementById('filtro-nivel-log').value;
    const busca = document.getElementById('filtro-busca-log').value.trim();

    if (nivel) parametros.set('nivel', nivel);
    if (busca) parametros.set('busca', busca);

    preConteudo.textContent = 'Carregando...';

    try {
        const resultado = await apiFetch(`/api/logs/conteudo?${parametros.toString()}`);

        if (resultado.truncado) {
            avisoTruncado.classList.remove('d-none');
        }

        if (!resultado.linhas.length) {
            avisoVazio.classList.remove('d-none');
            preConteudo.textContent = '';
            return;
        }

        preConteudo.innerHTML = resultado.linhas.map(function (linha) {
            const linhaEscapada = linha
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');

            if (linha.includes('[ERR]') || linha.includes('[FTL]')) {
                return `<span class="linha-erro">${linhaEscapada}</span>`;
            }
            if (linha.includes('[WRN]')) {
                return `<span class="linha-aviso">${linhaEscapada}</span>`;
            }
            return linhaEscapada;
        }).join('\n');

        preConteudo.scrollTop = preConteudo.scrollHeight;
    } catch (erro) {
        preConteudo.textContent = 'Erro ao carregar o log: ' + erro.message;
    }
}
