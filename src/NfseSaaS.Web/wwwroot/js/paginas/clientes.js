const podeEditarCliente = document.getElementById('btn-novo-cliente') !== null;

let tabelaClientes;
let modalCliente;
let empresaAtualId;

document.addEventListener('DOMContentLoaded', function () {
    const colunas = [
        { data: 'apelido' },
        { data: 'nome' },
        { data: 'cpfCnpj' },
        { data: 'codigoMunicipio' },
        {
            data: 'ativo',
            render: v => v
                ? '<span class="badge text-bg-success">Ativo</span>'
                : '<span class="badge text-bg-secondary">Inativo</span>'
        }
    ];

    if (podeEditarCliente) {
        colunas.push({
            data: null,
            orderable: false,
            render: (data, type, cliente) => `
                <button type="button" class="btn btn-sm btn-outline-primary btn-editar" data-id="${cliente.id}">
                    <i class="bi bi-pencil"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-warning btn-alternar-ativo" data-id="${cliente.id}" data-ativo="${cliente.ativo}">
                    <i class="bi ${cliente.ativo ? 'bi-toggle-on' : 'bi-toggle-off'}"></i>
                </button>
                <button type="button" class="btn btn-sm btn-outline-danger btn-excluir" data-id="${cliente.id}">
                    <i class="bi bi-trash"></i>
                </button>
            `
        });
    }

    tabelaClientes = new DataTable('#tabela-clientes', {
        columns: colunas,
    });

    configurarExportacao(tabelaClientes, 'Clientes');

    if (podeEditarCliente) {
        modalCliente = new bootstrap.Modal(document.getElementById('modal-cliente'));
        document.getElementById('btn-novo-cliente').addEventListener('click', abrirModalNovoCliente);
        document.getElementById('form-cliente').addEventListener('submit', salvarCliente);
        document.getElementById('btn-consultar-cnpj-cliente').addEventListener('click', consultarCnpjCliente);
        buscaCepCliente = configurarBuscaCep({
            cep: 'cep', logradouro: 'logradouro', bairro: 'bairro', complemento: 'complemento', uf: 'uf',
            codigoMunicipio: 'codigoMunicipio', nomeMunicipio: 'nome-municipio-cliente', statusCep: 'status-cep-cliente',
            numero: 'numero', botao: 'btn-buscar-cep-cliente'
        });
        configurarBuscaMunicipio({
            busca: 'busca-municipio-cliente', codigoMunicipio: 'codigoMunicipio', uf: 'uf',
            nomeMunicipio: 'nome-municipio-cliente', modal: 'modal-cliente'
        });
        document.getElementById('enviarNfsePorEmail').addEventListener('change', atualizarEnvioEmailCliente);
        document.getElementById('retemIss').addEventListener('change', atualizarObrigatoriedadeEndereco);
        ['cpfCnpj', ...CAMPOS_ENDERECO_CLIENTE, 'complemento'].forEach(id =>
            document.getElementById(id).addEventListener('input', atualizarObrigatoriedadeEndereco));

        document.getElementById('tabela-clientes').addEventListener('click', async function (e) {
            const botao = e.target.closest('button');
            if (!botao) return;
            const id = botao.dataset.id;

            await executarComBotaoDesabilitado(botao, async () => {
                if (botao.classList.contains('btn-editar')) await abrirModalEditarCliente(id);
                else if (botao.classList.contains('btn-alternar-ativo')) await alternarAtivoCliente(id, botao.dataset.ativo === 'true');
                else if (botao.classList.contains('btn-excluir')) await excluirCliente(id);
            });
        });
    }

    empresaAtualId = obterEmpresaAtualId();
    carregarClientes();
});

async function carregarClientes() {
    if (!empresaAtualId) return;

    try {
        const resultado = await apiFetch(`/api/clientes?empresaId=${empresaAtualId}&pageSize=200&incluirInativos=true`);
        tabelaClientes.clear();
        tabelaClientes.rows.add(resultado.items);
        tabelaClientes.draw();
    } catch (err) {
        mostrarErro(err.message);
    }
}

let buscaCepCliente = null;

function limparFormularioCliente() {
    document.getElementById('form-cliente').reset();
    buscaCepCliente?.reiniciar();
    document.getElementById('nome-municipio-cliente').textContent = 'Preenchido pelo CEP.';
    document.getElementById('avatar-modal-cliente').innerHTML = '<i class="bi bi-person"></i>';
    document.getElementById('subtitulo-modal-cliente').textContent = 'O tomador do serviço — como sai na nota.';
    document.getElementById('clienteId').value = '';
    document.getElementById('erro-cliente').classList.add('d-none');
    atualizarEnvioEmailCliente();
    atualizarObrigatoriedadeEndereco();
}

/**
 * Envio ligado: o e-mail vira obrigatório (o asterisco aparece sozinho
 * pelo CSS de [required]) e surgem as cópias. Desligado: as cópias
 * somem da tela, mas continuam no cadastro.
 */
function atualizarEnvioEmailCliente() {
    const ligado = document.getElementById('enviarNfsePorEmail').checked;
    document.getElementById('email').required = ligado;
    document.getElementById('bloco-copias-cliente').classList.toggle('d-none', !ligado);
}

const CAMPOS_ENDERECO_CLIENTE = ['cep', 'codigoMunicipio', 'logradouro', 'numero', 'bairro', 'uf'];

/**
 * Endereço do cliente (C3): obrigatório para CNPJ, para quem retém o ISS
 * e quando algum campo já foi preenchido (tudo ou nada). Pessoa física
 * sem retenção pode ficar sem endereço. O asterisco vem do CSS de [required].
 */
function atualizarObrigatoriedadeEndereco() {
    const documento = document.getElementById('cpfCnpj').value.replace(/[^0-9a-zA-Z]/g, '');
    const algumPreenchido = [...CAMPOS_ENDERECO_CLIENTE, 'complemento']
        .some(id => document.getElementById(id).value.trim() !== '');
    const obrigatorio = documento.length !== 11 || document.getElementById('retemIss').checked || algumPreenchido;
    CAMPOS_ENDERECO_CLIENTE.forEach(id => { document.getElementById(id).required = obrigatorio; });
}

/** "a@x.com, b@y.com; c@z.com" → ['a@x.com', 'b@y.com', 'c@z.com'] (sem vazios e sem repetidos). */
function lerEmailsCopiaCliente() {
    const vistos = new Set();
    return document.getElementById('emailsCopia').value
        .split(/[,;\s]+/)
        .map(e => e.trim())
        .filter(e => e && !vistos.has(e.toLowerCase()) && vistos.add(e.toLowerCase()));
}

function abrirModalNovoCliente() {
    limparFormularioCliente();
    document.getElementById('titulo-modal-cliente').textContent = 'Novo cliente';
    document.getElementById('cpfCnpj').disabled = false;
    modalCliente.show();
}

async function abrirModalEditarCliente(id) {
    limparFormularioCliente();
    document.getElementById('titulo-modal-cliente').textContent = 'Editar cliente';

    try {
        const cliente = await apiFetch(`/api/clientes/${id}`);

        document.getElementById('clienteId').value = cliente.id;
        document.getElementById('cpfCnpj').value = cliente.cpfCnpj;
        document.getElementById('cpfCnpj').disabled = true; // não editável, mesma regra da API
        document.getElementById('nome').value = cliente.nome;
        document.getElementById('apelido').value = cliente.apelido;
        document.getElementById('email').value = cliente.email ?? '';
        document.getElementById('telefone').value = cliente.telefone ?? '';
        document.getElementById('codigoMunicipio').value = cliente.codigoMunicipio;
        document.getElementById('cep').value = cliente.cep;
        document.getElementById('logradouro').value = cliente.logradouro;
        document.getElementById('numero').value = cliente.numero;
        document.getElementById('complemento').value = cliente.complemento ?? '';
        document.getElementById('bairro').value = cliente.bairro;
        document.getElementById('uf').value = cliente.uf;
        document.getElementById('retemIss').checked = cliente.retemIss === true;
        document.getElementById('enviarNfsePorEmail').checked = cliente.enviarNfsePorEmail === true;
        document.getElementById('emailsCopia').value = (cliente.emailsCopia || []).join(', ');
        document.getElementById(cliente.copiaOculta ? 'copiaCco' : 'copiaCc').checked = true;
        atualizarEnvioEmailCliente();
        atualizarObrigatoriedadeEndereco();

        const nomeExibicao = cliente.apelido || cliente.nome;
        document.getElementById('titulo-modal-cliente').textContent = nomeExibicao;
        document.getElementById('avatar-modal-cliente').textContent =
            nomeExibicao.split(/\s+/).filter(p => p.length > 1).slice(0, 2).map(p => p[0].toUpperCase()).join('') || '?';
        const doc = (cliente.cpfCnpj || '').replace(/\D/g, '');
        const docFormatado = doc.length === 14
            ? doc.replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5')
            : doc.replace(/(\d{3})(\d{3})(\d{3})(\d{2})/, '$1.$2.$3-$4');
        document.getElementById('subtitulo-modal-cliente').innerHTML =
            `${escaparHtml(docFormatado)} · ${cliente.ativo === false ? '<span class="badge text-bg-secondary">Inativo</span>' : '<span class="badge text-bg-success">Ativo</span>'}`;
        mostrarNomeMunicipio('codigoMunicipio', 'nome-municipio-cliente');

        modalCliente.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function montarPayloadCliente() {
    return {
        empresaId: empresaAtualId,
        nome: document.getElementById('nome').value,
        apelido: document.getElementById('apelido').value.trim(),
        email: document.getElementById('email').value || null,
        telefone: document.getElementById('telefone').value || null,
        codigoMunicipio: document.getElementById('codigoMunicipio').value,
        cep: document.getElementById('cep').value,
        logradouro: document.getElementById('logradouro').value,
        numero: document.getElementById('numero').value,
        complemento: document.getElementById('complemento').value || null,
        bairro: document.getElementById('bairro').value,
        uf: document.getElementById('uf').value.toUpperCase(),
        retemIss: document.getElementById('retemIss').checked,
        enviarNfsePorEmail: document.getElementById('enviarNfsePorEmail').checked,
        emailsCopia: lerEmailsCopiaCliente(),
        copiaOculta: document.getElementById('copiaCco').checked
    };
}

async function salvarCliente(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-cliente');

    const id = document.getElementById('clienteId').value;
    const payload = montarPayloadCliente();
    if (payload.emailsCopia.length > 5) {
        mostrarErroFormulario('erro-cliente', 'Informe no máximo 5 e-mails em cópia.');
        return;
    }

    try {
        if (id) {
            await apiFetch(`/api/clientes/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
        } else {
            payload.cpfCnpj = document.getElementById('cpfCnpj').value;
            await apiFetch('/api/clientes', { method: 'POST', body: JSON.stringify(payload) });
        }

        modalCliente.hide();
        await carregarClientes();
        mostrarToast('Cliente salvo com sucesso.');
    } catch (err) {
        mostrarErroFormulario('erro-cliente', err.message);
    }
}

async function alternarAtivoCliente(id, ativoAtualmente) {
    const acao = ativoAtualmente ? 'desativar' : 'reativar';
    try {
        await apiFetch(`/api/clientes/${id}/${acao}`, { method: 'POST' });
        await carregarClientes();
    } catch (err) {
        mostrarErro(err.message);
    }
}

async function excluirCliente(id) {
    const confirmado = await confirmarAcao(
        'Excluir este cliente? Só funciona se ele não tiver Nfse vinculada.',
        { titulo: 'Excluir cliente', textoBotao: 'Excluir', variante: 'perigo' }
    );
    if (!confirmado) return;

    try {
        await apiFetch(`/api/clientes/${id}`, { method: 'DELETE' });
        await carregarClientes();
        mostrarToast('Cliente excluído.');
    } catch (err) {
        mostrarErro(err.message);
    }
}

// Só funciona pra CNPJ (14 dígitos) — CPF de pessoa física não tem
// consulta pública de endereço na Receita. Mesmo endpoint já usado na
// tela de Empresa (/api/consultas/cnpj/{cnpj}), reaproveitado aqui.
async function consultarCnpjCliente() {
    const campoCpfCnpj = document.getElementById('cpfCnpj');
    const botao = document.getElementById('btn-consultar-cnpj-cliente');
    const documento = campoCpfCnpj.value.replace(/\D/g, '');

    if (documento.length !== 14) {
        mostrarToast('A busca automática só funciona com CNPJ (14 dígitos) — CPF não tem endereço público na Receita.', 'erro');
        return;
    }

    botao.disabled = true;
    const iconeOriginal = botao.innerHTML;
    botao.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';

    try {
        const dados = await apiFetch(`/api/consultas/cnpj/${documento}`);

        document.getElementById('nome').value = dados.razaoSocial ?? '';
        // Sugere o nome fantasia como Apelido só se o campo estiver vazio
        // — nunca sobrescreve um apelido que o usuário já escolheu.
        const campoApelido = document.getElementById('apelido');
        if (!campoApelido.value.trim() && dados.nomeFantasia) campoApelido.value = dados.nomeFantasia.trim().slice(0, 40);
        if (dados.telefone) document.getElementById('telefone').value = dados.telefone;
        if (dados.email) document.getElementById('email').value = dados.email;
        if (dados.codigoMunicipio) document.getElementById('codigoMunicipio').value = dados.codigoMunicipio;
        if (dados.cep) document.getElementById('cep').value = dados.cep;
        document.getElementById('logradouro').value = dados.logradouro ?? '';
        document.getElementById('numero').value = dados.numero ?? '';
        document.getElementById('complemento').value = dados.complemento ?? '';
        document.getElementById('bairro').value = dados.bairro ?? '';
        if (dados.uf) document.getElementById('uf').value = dados.uf;
        mostrarNomeMunicipio('codigoMunicipio', 'nome-municipio-cliente');

        mostrarToast(
            `Endereço preenchido a partir da Receita Federal${dados.situacaoCadastral ? ' — situação: ' + dados.situacaoCadastral : ''}. Confira antes de salvar.`
        );
    } catch (err) {
        mostrarErro(err.message);
    } finally {
        botao.disabled = false;
        botao.innerHTML = iconeOriginal;
    }
}
