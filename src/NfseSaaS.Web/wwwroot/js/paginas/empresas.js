const podeEditar = document.getElementById('btn-nova-empresa') !== null;

// Mesmos valores numéricos do enum TipoAmbiente no C# (1 = Producao,
// 2 = Homologacao) — a API não traduz isso pra string, só serializa o
// int (mesmo padrão de NfseStatus em nfse.js).
const AMBIENTE_PRODUCAO = 1;
const AMBIENTE_HOMOLOGACAO = 2;
const ROTULOS_AMBIENTE = {
    [AMBIENTE_PRODUCAO]: { texto: 'Produção', cor: 'danger' },
    [AMBIENTE_HOMOLOGACAO]: { texto: 'Homologação', cor: 'warning' }
};

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
            data: 'tipoAmbiente',
            render: v => {
                const rotulo = ROTULOS_AMBIENTE[v] ?? { texto: 'Desconhecido', cor: 'secondary' };
                return `<span class="badge text-bg-${rotulo.cor}">${rotulo.texto}</span>`;
            }
        },
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
                ${empresa.tipoAmbiente === AMBIENTE_HOMOLOGACAO ? `
                <button type="button" class="btn btn-sm btn-outline-danger btn-alterar-ambiente" data-id="${empresa.id}" data-nome="${empresa.razaoSocial}" data-novo-ambiente="${AMBIENTE_PRODUCAO}" title="Promover para Produção">
                    <i class="bi bi-rocket-takeoff"></i>
                </button>` : `
                <button type="button" class="btn btn-sm btn-outline-secondary btn-alterar-ambiente" data-id="${empresa.id}" data-nome="${empresa.razaoSocial}" data-novo-ambiente="${AMBIENTE_HOMOLOGACAO}" title="Voltar para Homologação">
                    <i class="bi bi-arrow-counterclockwise"></i>
                </button>`}
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

        document.getElementById('tabs-empresa').addEventListener('click', function (e) {
            const botao = e.target.closest('button[data-tab-alvo]');
            if (botao) trocarAbaEmpresa(botao.dataset.tabAlvo);
        });

        document.getElementById('automacaoFrequencia').addEventListener('change', aplicarRegraFrequenciaAutomacao);

        document.getElementById('tabela-empresas').addEventListener('click', async function (e) {
            const botao = e.target.closest('button');
            if (!botao) return;
            const id = botao.dataset.id;

            await executarComBotaoDesabilitado(botao, async () => {
                if (botao.classList.contains('btn-editar')) await abrirModalEditarEmpresa(id);
                else if (botao.classList.contains('btn-certificado')) await abrirModalCertificado(id, botao.dataset.nome);
                else if (botao.classList.contains('btn-alterar-ambiente')) await alterarAmbiente(id, botao.dataset.nome, parseInt(botao.dataset.novoAmbiente, 10));
                else if (botao.classList.contains('btn-alternar-ativo')) await alternarAtivo(id, botao.dataset.ativo === 'true');
                else if (botao.classList.contains('btn-excluir')) await excluirEmpresa(id);
            });
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
    trocarAbaEmpresa('tab-dados-empresa');

    document.getElementById('automacaoAtivo').checked = false;
    document.getElementById('automacaoFrequencia').value = '2';
    document.getElementById('automacaoDiaSemana').value = '1';
    document.getElementById('automacaoDiaDoMes').value = '1';
    document.getElementById('automacaoHorario').value = '09:00';
    document.getElementById('automacaoModo').value = '1';
    aplicarRegraFrequenciaAutomacao();
}

// Mostra só o campo (dia da semana ou dia do mês) que faz sentido pra
// frequência escolhida — mesmo raciocínio de aplicarRegraTipoCobranca
// em contratos.js (Avulso/Mensal), aqui pra Diária/Semanal/Mensal.
function aplicarRegraFrequenciaAutomacao() {
    const frequencia = document.getElementById('automacaoFrequencia').value; // "0" Diária, "1" Semanal, "2" Mensal
    document.getElementById('campo-automacao-dia-semana').classList.toggle('d-none', frequencia !== '1');
    document.getElementById('campo-automacao-dia-mes').classList.toggle('d-none', frequencia !== '2');
}

// Mesmo padrão de trocarAbaContrato (contratos.js) — troca a aba ativa
// no cabeçalho e mostra só o painel correspondente.
function trocarAbaEmpresa(alvo) {
    document.querySelectorAll('#tabs-empresa .nav-link').forEach(b => b.classList.toggle('active', b.dataset.tabAlvo === alvo));
    document.querySelectorAll('.tab-conteudo-empresa').forEach(p => p.classList.toggle('d-none', p.dataset.tab !== alvo));
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

        const automacao = await apiFetch(`/api/empresas/${id}/automacao-nota-mensal`);
        document.getElementById('automacaoAtivo').checked = automacao.ativo;
        document.getElementById('automacaoFrequencia').value = automacao.frequencia;
        if (automacao.diaSemana !== null) document.getElementById('automacaoDiaSemana').value = automacao.diaSemana;
        if (automacao.diaDoMes !== null) document.getElementById('automacaoDiaDoMes').value = automacao.diaDoMes;
        document.getElementById('automacaoHorario').value = automacao.horario.substring(0, 5); // "HH:mm:ss" -> "HH:mm"
        document.getElementById('automacaoModo').value = automacao.modo;
        aplicarRegraFrequenciaAutomacao();

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

function montarPayloadAutomacao() {
    const frequencia = document.getElementById('automacaoFrequencia').value;

    return {
        ativo: document.getElementById('automacaoAtivo').checked,
        frequencia: parseInt(frequencia, 10),
        // Só manda o campo que se aplica à frequência escolhida — o
        // outro vai null (o validador do servidor exige exatamente
        // isso, ver AtualizarConfiguracaoAutomacaoNotaMensalRequestValidator).
        diaSemana: frequencia === '1' ? parseInt(document.getElementById('automacaoDiaSemana').value, 10) : null,
        diaDoMes: frequencia === '2' ? parseInt(document.getElementById('automacaoDiaDoMes').value, 10) : null,
        // <input type="time"> devolve "HH:mm" — completa com ":00" pra
        // bater com o formato que o conversor padrão de TimeOnly do
        // System.Text.Json espera ("HH:mm:ss").
        horario: `${document.getElementById('automacaoHorario').value}:00`,
        modo: parseInt(document.getElementById('automacaoModo').value, 10)
    };
}

async function salvarEmpresa(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-empresa');

    if (!validarFormularioComAbas(document.getElementById('form-empresa'), '.tab-conteudo-empresa', trocarAbaEmpresa, 'erro-empresa')) {
        return;
    }

    const id = document.getElementById('empresaId').value;
    const payload = montarPayloadEmpresa();

    try {
        let empresaId = id;

        if (id) {
            await apiFetch(`/api/empresas/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            payload.cnpj = document.getElementById('cnpj').value;
            const resposta = await apiFetch('/api/empresas', { method: 'POST', body: JSON.stringify(payload) });
            empresaId = resposta.empresaId;
        }

        // Sub-recurso próprio (GET/PUT /api/empresas/{id}/automacao-nota-mensal),
        // salvo à parte — mesmo empresaId de cima, tanto faz se a Empresa
        // é nova ou já existia.
        await apiFetch(`/api/empresas/${empresaId}/automacao-nota-mensal`, {
            method: 'PUT',
            body: JSON.stringify(montarPayloadAutomacao())
        });

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

async function alterarAmbiente(id, nomeEmpresa, novoAmbiente) {
    const indoPraProducao = novoAmbiente === AMBIENTE_PRODUCAO;
    const mensagem = indoPraProducao
        ? `Promover "${nomeEmpresa}" para Produção? A partir de agora, toda nota emitida por ela terá efeito fiscal real.`
        : `Voltar "${nomeEmpresa}" para Homologação? A partir de agora, as notas emitidas por ela voltam a ser de teste (sem efeito fiscal). Notas já emitidas em Produção continuam valendo — isso só afeta o que for emitido daqui pra frente.`;

    const confirmado = await confirmarAcao(mensagem, {
        titulo: indoPraProducao ? 'Promover para Produção' : 'Voltar para Homologação',
        textoBotao: indoPraProducao ? 'Promover' : 'Voltar',
        variante: 'perigo'
    });
    if (!confirmado) return;

    try {
        await apiFetch(`/api/empresas/${id}/ambiente`, {
            method: 'PUT',
            body: JSON.stringify({ tipoAmbiente: novoAmbiente })
        });

        // Se a empresa alterada é a que está selecionada no topo, o
        // badge lá em cima também precisa refletir o ambiente novo —
        // recarregar é o jeito mais simples de manter tudo consistente
        // (mesmo raciocínio de empresa-atual.js ao trocar a seleção).
        if (id === obterEmpresaAtualId()) {
            location.reload();
            return;
        }

        await carregarEmpresas();
        mostrarToast(`"${nomeEmpresa}" agora está em ${indoPraProducao ? 'Produção' : 'Homologação'}.`);
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
            headers: obterCsrfHeader(),
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
