const podeEditar = document.getElementById('btn-nova-empresa') !== null;

let tabela;
let modalEmpresa;
let modalCertificado;
let empresaCertificadoAtualId;

document.addEventListener('DOMContentLoaded', function () {
    const colunas = [
        { data: 'razaoSocial' },
        { data: 'cnpj' },
        { data: 'inscricaoMunicipal' },
        { data: 'codigoMunicipio' },
        {
            data: 'ativo',
            render: v => v
                ? '<span class="badge text-bg-success">Ativa</span>'
                : '<span class="badge text-bg-secondary">Inativa</span>'
        }
    ];

    if (podeEditar) {
        colunas.push({
            data: null,
            orderable: false,
            render: (data, type, empresa) => `
                <button type="button" class="btn btn-sm btn-outline-primary btn-editar" data-id="${empresa.id}">
                    <i class="bi bi-pencil"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-info btn-certificado" data-id="${empresa.id}" data-nome="${empresa.razaoSocial}">
                    <i class="bi bi-file-earmark-lock"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-warning btn-alternar-ativo" data-id="${empresa.id}" data-ativo="${empresa.ativo}">
                    <i class="bi ${empresa.ativo ? 'bi-toggle-on' : 'bi-toggle-off'}"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-danger btn-excluir" data-id="${empresa.id}">
                    <i class="bi bi-trash"></i>
                </button>
            `
        });
    }

    tabela = new DataTable('#tabela-empresas', {
        columns: colunas,
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    configurarExportacao(tabela, 'Empresas');

    if (podeEditar) {
        modalEmpresa = new bootstrap.Modal(document.getElementById('modal-empresa'));
        modalCertificado = new bootstrap.Modal(document.getElementById('modal-certificado'));

        document.getElementById('btn-nova-empresa').addEventListener('click', abrirModalNovaEmpresa);
        document.getElementById('btn-consultar-cnpj').addEventListener('click', consultarCnpj);
        document.getElementById('form-empresa').addEventListener('submit', salvarEmpresa);
        document.getElementById('form-certificado').addEventListener('submit', enviarCertificado);
        document.getElementById('btn-testar-conexao').addEventListener('click', testarConexaoCertificado);

        document.getElementById('tabela-empresas').addEventListener('click', async function (e) {
            const botao = e.target.closest('button');
            if (!botao) return;
            const id = botao.dataset.id;

            if (botao.classList.contains('btn-editar')) await abrirModalEditarEmpresa(id);
            else if (botao.classList.contains('btn-certificado')) await abrirModalCertificado(id, botao.dataset.nome);
            else if (botao.classList.contains('btn-alternar-ativo')) await alternarAtivo(id, botao.dataset.ativo === 'true');
            else if (botao.classList.contains('btn-excluir')) await excluirEmpresa(id);
        });
    }

    carregarEmpresas();
});

async function carregarEmpresas() {
    try {
        // pageSize alto + incluirInativas=true: a paginação/filtro real
        // fica a cargo do DataTable no navegador (client-side processing).
        const resultado = await apiFetch('/api/empresas?pageSize=200&incluirInativas=true');
        tabela.clear();
        tabela.rows.add(resultado.items);
        tabela.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function limparFormularioEmpresa() {
    document.getElementById('form-empresa').reset();
    document.getElementById('empresaId').value = '';
    document.getElementById('erro-empresa').classList.add('d-none');
}

function abrirModalNovaEmpresa() {
    limparFormularioEmpresa();
    document.getElementById('titulo-modal-empresa').textContent = 'Nova empresa';
    document.getElementById('cnpj').disabled = false;
    modalEmpresa.show();
}

async function abrirModalEditarEmpresa(id) {
    limparFormularioEmpresa();
    document.getElementById('titulo-modal-empresa').textContent = 'Editar empresa';

    try {
        const empresa = await apiFetch(`/api/empresas/${id}`);

        document.getElementById('empresaId').value = empresa.id;
        document.getElementById('cnpj').value = empresa.cnpj;
        document.getElementById('cnpj').disabled = true; // não editável, mesma regra da API
        document.getElementById('razaoSocial').value = empresa.razaoSocial;
        document.getElementById('nomeFantasia').value = empresa.nomeFantasia ?? '';
        document.getElementById('inscricaoMunicipal').value = empresa.inscricaoMunicipal;
        document.getElementById('codigoMunicipio').value = empresa.codigoMunicipio;
        document.getElementById('telefone').value = empresa.telefone;
        document.getElementById('email').value = empresa.email;
        document.getElementById('cep').value = empresa.cep;
        document.getElementById('logradouro').value = empresa.logradouro;
        document.getElementById('numero').value = empresa.numero;
        document.getElementById('complemento').value = empresa.complemento ?? '';
        document.getElementById('bairro').value = empresa.bairro;
        document.getElementById('uf').value = empresa.uf;
        document.getElementById('opSimpNac').value = empresa.opSimpNac;
        document.getElementById('regApTribSN').value = empresa.regApTribSN;
        document.getElementById('regEspTrib').value = empresa.regEspTrib;
        document.getElementById('tribIssqn').value = empresa.tribIssqn;
        document.getElementById('tpRetIssqn').value = empresa.tpRetIssqn;
        document.getElementById('cstPisCofins').value = empresa.cstPisCofins;
        document.getElementById('tpRetPisCofins').value = empresa.tpRetPisCofins;
        document.getElementById('percentualTotalTributosSimplesNacional').value = empresa.percentualTotalTributosSimplesNacional ?? '';
        document.getElementById('diasAlertaReajusteContratoPadrao').value = empresa.diasAlertaReajusteContratoPadrao;

        modalEmpresa.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function montarPayloadEmpresa() {
    return {
        razaoSocial: document.getElementById('razaoSocial').value,
        nomeFantasia: document.getElementById('nomeFantasia').value || null,
        inscricaoMunicipal: document.getElementById('inscricaoMunicipal').value,
        codigoMunicipio: document.getElementById('codigoMunicipio').value,
        telefone: document.getElementById('telefone').value,
        email: document.getElementById('email').value,
        cep: document.getElementById('cep').value,
        logradouro: document.getElementById('logradouro').value,
        numero: document.getElementById('numero').value,
        complemento: document.getElementById('complemento').value || null,
        bairro: document.getElementById('bairro').value,
        uf: document.getElementById('uf').value.toUpperCase(),
        opSimpNac: document.getElementById('opSimpNac').value,
        regApTribSN: document.getElementById('regApTribSN').value,
        regEspTrib: document.getElementById('regEspTrib').value,
        tribIssqn: document.getElementById('tribIssqn').value,
        tpRetIssqn: document.getElementById('tpRetIssqn').value,
        cstPisCofins: document.getElementById('cstPisCofins').value,
        tpRetPisCofins: document.getElementById('tpRetPisCofins').value,
        percentualTotalTributosSimplesNacional: document.getElementById('percentualTotalTributosSimplesNacional').value || '',
        diasAlertaReajusteContratoPadrao: parseInt(document.getElementById('diasAlertaReajusteContratoPadrao').value, 10)
    };
}

async function salvarEmpresa(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-empresa');

    const id = document.getElementById('empresaId').value;
    const payload = montarPayloadEmpresa();

    try {
        if (id) {
            await apiFetch(`/api/empresas/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            payload.cnpj = document.getElementById('cnpj').value;
            await apiFetch('/api/empresas', { method: 'POST', body: JSON.stringify(payload) });
        }

        modalEmpresa.hide();
        await carregarEmpresas();
        mostrarToast('Empresa salva com sucesso.');
    } catch (err) {
        mostrarErroFormulario('erro-empresa', err.message);
    }
}

async function alternarAtivo(id, ativoAtualmente) {
    const acao = ativoAtualmente ? 'desativar' : 'reativar';
    try {
        await apiFetch(`/api/empresas/${id}/${acao}`, { method: 'POST' });
        await carregarEmpresas();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function excluirEmpresa(id) {
    const confirmado = await confirmarAcao(
        'Excluir esta empresa? Só funciona se ela não tiver Cliente, Serviço ou Nfse vinculados.',
        { titulo: 'Excluir empresa', textoBotao: 'Excluir', variante: 'perigo' }
    );
    if (!confirmado) return;

    try {
        await apiFetch(`/api/empresas/${id}`, { method: 'DELETE' });
        await carregarEmpresas();
        mostrarToast('Empresa excluída.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function abrirModalCertificado(id, nomeEmpresa) {
    empresaCertificadoAtualId = id;

    document.getElementById('titulo-modal-certificado').textContent = `Certificado — ${nomeEmpresa}`;
    document.getElementById('form-certificado').reset();
    ocultarErroFormulario('erro-certificado');
    ocultarResultado('resultado-teste-conexao');
    document.getElementById('status-certificado').innerHTML = '<span class="text-muted">Carregando status...</span>';
    document.getElementById('btn-testar-conexao').disabled = true;

    modalCertificado.show();
    await carregarStatusCertificado();
}

async function carregarStatusCertificado() {
    const statusDiv = document.getElementById('status-certificado');

    try {
        const status = await apiFetch(`/api/empresas/${empresaCertificadoAtualId}/certificado`);

        if (!status.existe) {
            statusDiv.innerHTML = '<span class="badge text-bg-secondary">Sem certificado cadastrado</span>';
            document.getElementById('btn-testar-conexao').disabled = true;
            return;
        }

        const corBadge = status.valido ? 'success' : 'danger';
        const textoBadge = status.valido ? 'Válido' : 'Vencido/inválido';

        statusDiv.innerHTML = `
            <span class="badge text-bg-${corBadge}">${textoBadge}</span>
            <div class="small text-muted mt-1">
                ${status.subject ?? ''}<br />
                Validade: ${new Date(status.validoDe).toLocaleDateString('pt-BR')} a ${new Date(status.validoAte).toLocaleDateString('pt-BR')}
            </div>
        `;
        document.getElementById('btn-testar-conexao').disabled = !status.valido;
    } catch (err) {
        statusDiv.innerHTML = '<span class="text-danger">Não foi possível carregar o status.</span>';
    }
}

async function enviarCertificado(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-certificado');
    ocultarResultado('resultado-teste-conexao');

    const arquivo = document.getElementById('certificado-arquivo').files[0];
    const senha = document.getElementById('certificado-senha').value;

    if (!arquivo) {
        mostrarErroFormulario('erro-certificado', 'Selecione o arquivo .pfx.');
        return;
    }

    const formData = new FormData();
    formData.append('arquivo', arquivo);
    formData.append('senha', senha);

    const botaoEnviar = e.target.querySelector('button[type="submit"]');
    botaoEnviar.disabled = true;

    try {
        // Upload multipart — não usa apiFetch (que sempre manda
        // Content-Type: application/json); o navegador define o
        // boundary do multipart/form-data automaticamente.
        const resposta = await fetch(`/api/empresas/${empresaCertificadoAtualId}/certificado`, {
            method: 'POST',
            credentials: 'same-origin',
            body: formData
        });

        if (!resposta.ok) {
            const corpo = await resposta.json().catch(() => null);
            throw new Error(extrairMensagemDeErro(corpo) ?? `Erro ${resposta.status} ao enviar o certificado.`);
        }

        document.getElementById('form-certificado').reset();
        await carregarStatusCertificado();
        mostrarToast('Certificado enviado com sucesso.');
    } catch (err) {
        mostrarErroFormulario('erro-certificado', err.message);
    } finally {
        botaoEnviar.disabled = false;
    }
}

async function testarConexaoCertificado() {
    const botao = document.getElementById('btn-testar-conexao');

    ocultarResultado('resultado-teste-conexao');
    botao.disabled = true;
    botao.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Testando...';

    try {
        const resultado = await apiFetch(`/api/empresas/${empresaCertificadoAtualId}/certificado/testar-conexao`, { method: 'POST' });
        mostrarResultado('resultado-teste-conexao', resultado.sucesso, resultado.mensagem);
    } catch (err) {
        mostrarResultado('resultado-teste-conexao', false, err.message);
    } finally {
        botao.disabled = false;
        botao.innerHTML = 'Testar conexão';
    }
}

async function consultarCnpj() {
    const campoCnpj = document.getElementById('cnpj');
    const botao = document.getElementById('btn-consultar-cnpj');
    const cnpj = campoCnpj.value.replace(/\D/g, '');

    if (cnpj.length !== 14) {
        mostrarToast('Informe os 14 dígitos do CNPJ antes de consultar.', 'erro');
        return;
    }

    botao.disabled = true;
    const iconeOriginal = botao.innerHTML;
    botao.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';

    try {
        const dados = await apiFetch(`/api/consultas/cnpj/${cnpj}`);

        document.getElementById('razaoSocial').value = dados.razaoSocial ?? '';
        document.getElementById('nomeFantasia').value = dados.nomeFantasia ?? '';
        if (dados.codigoMunicipio) document.getElementById('codigoMunicipio').value = dados.codigoMunicipio;
        if (dados.telefone) document.getElementById('telefone').value = dados.telefone;
        if (dados.email) document.getElementById('email').value = dados.email;
        if (dados.cep) document.getElementById('cep').value = dados.cep;
        document.getElementById('logradouro').value = dados.logradouro ?? '';
        document.getElementById('numero').value = dados.numero ?? '';
        document.getElementById('complemento').value = dados.complemento ?? '';
        document.getElementById('bairro').value = dados.bairro ?? '';
        if (dados.uf) document.getElementById('uf').value = dados.uf;

        mostrarToast(
            `Dados preenchidos a partir da Receita Federal${dados.situacaoCadastral ? ' — situação: ' + dados.situacaoCadastral : ''}. ` +
            `Confira a Inscrição Municipal e o regime tributário manualmente (a Receita não informa isso).`
        );
    } catch (err) {
        mostrarErro(err.message);
    } finally {
        botao.disabled = false;
        botao.innerHTML = iconeOriginal;
    }
}
