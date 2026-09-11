const podeEditarCliente = document.getElementById('btn-novo-cliente') !== null;

let tabelaClientes;
let modalCliente;
let empresaAtualId;

document.addEventListener('DOMContentLoaded', function () {
    const colunas = [
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
        language: { url: 'https://cdn.datatables.net/plug-ins/2.1.8/i18n/pt-BR.json' }
    });

    configurarExportacao(tabelaClientes, 'Clientes');

    if (podeEditarCliente) {
        modalCliente = new bootstrap.Modal(document.getElementById('modal-cliente'));
        document.getElementById('btn-novo-cliente').addEventListener('click', abrirModalNovoCliente);
        document.getElementById('form-cliente').addEventListener('submit', salvarCliente);
        document.getElementById('btn-consultar-cnpj-cliente').addEventListener('click', consultarCnpjCliente);

        document.getElementById('tabela-clientes').addEventListener('click', async function (e) {
            const botao = e.target.closest('button');
            if (!botao) return;
            const id = botao.dataset.id;

            if (botao.classList.contains('btn-editar')) await abrirModalEditarCliente(id);
            else if (botao.classList.contains('btn-alternar-ativo')) await alternarAtivoCliente(id, botao.dataset.ativo === 'true');
            else if (botao.classList.contains('btn-excluir')) await excluirCliente(id);
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

function limparFormularioCliente() {
    document.getElementById('form-cliente').reset();
    document.getElementById('clienteId').value = '';
    document.getElementById('erro-cliente').classList.add('d-none');
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
        document.getElementById('email').value = cliente.email ?? '';
        document.getElementById('telefone').value = cliente.telefone ?? '';
        document.getElementById('codigoMunicipio').value = cliente.codigoMunicipio;
        document.getElementById('cep').value = cliente.cep;
        document.getElementById('logradouro').value = cliente.logradouro;
        document.getElementById('numero').value = cliente.numero;
        document.getElementById('complemento').value = cliente.complemento ?? '';
        document.getElementById('bairro').value = cliente.bairro;
        document.getElementById('uf').value = cliente.uf;

        modalCliente.show();
    } catch (err) {
        mostrarErro(err.message);
    }
}

function montarPayloadCliente() {
    return {
        empresaId: empresaAtualId,
        nome: document.getElementById('nome').value,
        email: document.getElementById('email').value || null,
        telefone: document.getElementById('telefone').value || null,
        codigoMunicipio: document.getElementById('codigoMunicipio').value,
        cep: document.getElementById('cep').value,
        logradouro: document.getElementById('logradouro').value,
        numero: document.getElementById('numero').value,
        complemento: document.getElementById('complemento').value || null,
        bairro: document.getElementById('bairro').value,
        uf: document.getElementById('uf').value.toUpperCase()
    };
}

async function salvarCliente(e) {
    e.preventDefault();
    ocultarErroFormulario('erro-cliente');

    const id = document.getElementById('clienteId').value;
    const payload = montarPayloadCliente();

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
        if (dados.telefone) document.getElementById('telefone').value = dados.telefone;
        if (dados.email) document.getElementById('email').value = dados.email;
        if (dados.codigoMunicipio) document.getElementById('codigoMunicipio').value = dados.codigoMunicipio;
        if (dados.cep) document.getElementById('cep').value = dados.cep;
        document.getElementById('logradouro').value = dados.logradouro ?? '';
        document.getElementById('numero').value = dados.numero ?? '';
        document.getElementById('complemento').value = dados.complemento ?? '';
        document.getElementById('bairro').value = dados.bairro ?? '';
        if (dados.uf) document.getElementById('uf').value = dados.uf;

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
