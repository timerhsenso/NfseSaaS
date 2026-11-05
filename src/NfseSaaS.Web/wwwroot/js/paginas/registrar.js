// Cadastro em etapas: 1) você é (empresa/contador) 2) CNPJ (busca na Receita) ou CPF 3) e-mail e senha.
// Perfil Empresa: a conta já nasce com a Empresa cadastrada (AuthController.Registrar).
const PERFIL_EMPRESA = 1, PERFIL_CONTADOR = 2;

const cadastro = { perfil: null, documento: '', nome: '', encontrada: false };
let buscaAtual = null;

// ---------- Documento (mesmas regras do DocumentoFiscalValidator) ----------

function normalizarDocumento(texto) {
    return (texto || '').toUpperCase().replace(/[^0-9A-Z]/g, '');
}

function cpfValido(cpf) {
    if (!/^\d{11}$/.test(cpf) || /^(\d)\1{10}$/.test(cpf)) return false;
    const digito = (base, peso) => {
        const soma = [...base].reduce((t, c, i) => t + Number(c) * (peso - i), 0);
        const resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    };
    const d1 = digito(cpf.slice(0, 9), 10);
    const d2 = digito(cpf.slice(0, 9) + d1, 11);
    return cpf.endsWith(`${d1}${d2}`);
}

// CNPJ numérico ou alfanumérico (letras nas 12 primeiras posições; valor = código ASCII - 48).
function cnpjValido(cnpj) {
    if (!/^[0-9A-Z]{12}\d{2}$/.test(cnpj) || /^(\d)\1{13}$/.test(cnpj)) return false;
    const digito = (base) => {
        const pesos = base.length === 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        const soma = [...base].reduce((t, c, i) => t + (c.charCodeAt(0) - 48) * pesos[i], 0);
        const resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    };
    const d1 = digito(cnpj.slice(0, 12));
    const d2 = digito(cnpj.slice(0, 12) + d1);
    return cnpj.endsWith(`${d1}${d2}`);
}

function formatarDocumento(d) {
    if (d.length === 14) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`;
    if (d.length === 11) return `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}`;
    return d;
}

// ---------- Navegação ----------

function irParaEtapa(n) {
    document.querySelectorAll('[data-etapa]').forEach(s => s.classList.toggle('d-none', Number(s.dataset.etapa) !== n));
    document.getElementById('cadastro-etapa-numero').textContent = `Etapa ${n} de 3`;
    document.getElementById('cadastro-progresso').style.width = `${Math.round(n * 100 / 3)}%`;
    esconderErro();
    if (n === 2) document.getElementById('documento').focus();
    if (n === 3) {
        document.getElementById('resumo-conta').textContent = `${cadastro.nome} (${formatarDocumento(cadastro.documento)})`;
        document.getElementById('email').focus();
    }
}

function mostrarErroCadastro(texto) {
    const erro = document.getElementById('erro-registrar');
    erro.textContent = texto;
    erro.classList.remove('d-none');
}

function esconderErro() {
    document.getElementById('erro-registrar').classList.add('d-none');
}

function aviso(texto) {
    const div = document.getElementById('aviso-documento');
    div.innerHTML = texto ?? '';
    div.classList.toggle('d-none', !texto);
}

function mostrarCampoNome(mostrar, valor) {
    document.getElementById('grupo-nome').classList.toggle('d-none', !mostrar);
    if (valor !== undefined) document.getElementById('nome').value = valor;
}

function atualizarContinuar() {
    const nome = document.getElementById('nome').value.trim();
    const pronto = cadastro.documento && (cadastro.encontrada || nome.length >= 2);
    document.getElementById('btn-continuar-documento').disabled = !pronto;
}

function escolherPerfil(perfil) {
    cadastro.perfil = perfil;
    const contador = perfil === PERFIL_CONTADOR;
    document.getElementById('rotulo-documento').textContent = contador ? 'Seu CPF ou o CNPJ do escritório' : 'CNPJ da empresa';
    document.getElementById('documento').placeholder = contador ? 'CPF ou CNPJ' : '00.000.000/0000-00';
    document.getElementById('ajuda-documento').textContent = contador
        ? 'Com CNPJ buscamos o nome na Receita; com CPF é só digitar seu nome.'
        : 'Digite os números; buscamos os dados na Receita Federal.';
    document.getElementById('rotulo-nome').textContent = contador ? 'Seu nome ou o nome do escritório' : 'Nome da empresa (razão social)';
    limparDocumento();
    document.getElementById('documento').value = '';
    irParaEtapa(2);
}

function limparDocumento() {
    cadastro.documento = '';
    cadastro.nome = '';
    cadastro.encontrada = false;
    document.getElementById('empresa-encontrada').classList.add('d-none');
    aviso(null);
    mostrarCampoNome(false, '');
    atualizarContinuar();
}

// ---------- Busca do CNPJ (rápida: se falhar ou demorar, a pessoa digita o nome) ----------

async function buscarCnpj(cnpj) {
    buscaAtual?.abort();
    const controle = new AbortController();
    buscaAtual = controle;
    const tempo = setTimeout(() => controle.abort(), 6000);
    document.getElementById('buscando-documento').classList.remove('d-none');

    try {
        const resposta = await fetch(`/api/cadastro/cnpj/${encodeURIComponent(cnpj)}`, { signal: controle.signal, credentials: 'same-origin' });
        if (normalizarDocumento(document.getElementById('documento').value) !== cnpj) return; // mudou enquanto buscava
        const corpo = await resposta.json().catch(() => null);

        if (resposta.ok && corpo) {
            if (corpo.jaTemConta) {
                aviso('Já existe uma conta para este CNPJ. <a href="/Account/Login">Entre</a> ou peça um convite ao administrador dela.');
                return;
            }
            if (!corpo.ativa) {
                aviso(`A Receita Federal mostra este CNPJ como "${escaparHtml(corpo.situacaoCadastral ?? 'irregular')}". Empresa nessa situação não emite nota fiscal.`);
                return;
            }
            cadastro.documento = cnpj;
            cadastro.encontrada = true;
            cadastro.nome = corpo.razaoSocial;
            document.getElementById('empresa-encontrada-nome').textContent = corpo.nomeFantasia ? `${corpo.razaoSocial} (${corpo.nomeFantasia})` : corpo.razaoSocial;
            document.getElementById('empresa-encontrada-local').textContent = [corpo.municipio, corpo.uf].filter(Boolean).join('/');
            document.getElementById('empresa-encontrada').classList.remove('d-none');
            const continuar = document.getElementById('btn-continuar-documento');
            continuar.textContent = 'É essa, continuar';
            atualizarContinuar();
            continuar.focus();
            return;
        }

        // Não achou, limite de consultas ou serviço fora: segue digitando o nome.
        cadastro.documento = cnpj;
        aviso(resposta.status === 404
            ? 'Não encontramos este CNPJ na Receita agora. Confira os números ou digite o nome para seguir.'
            : 'Não deu para buscar os dados agora. Digite o nome para seguir; depois você completa na tela da empresa.');
        mostrarCampoNome(true, '');
        document.getElementById('nome').focus();
    } catch {
        if (controle.signal.aborted && buscaAtual !== controle) return;
        cadastro.documento = cnpj;
        aviso('A busca demorou. Digite o nome para seguir; depois você completa na tela da empresa.');
        mostrarCampoNome(true, '');
    } finally {
        clearTimeout(tempo);
        if (buscaAtual === controle) document.getElementById('buscando-documento').classList.add('d-none');
        atualizarContinuar();
    }
}

function aoDigitarDocumento() {
    const campo = document.getElementById('documento');
    const documento = normalizarDocumento(campo.value);
    document.getElementById('btn-continuar-documento').textContent = 'Continuar';
    limparDocumento();

    if (documento.length === 14) {
        campo.value = formatarDocumento(documento);
        if (!cnpjValido(documento)) { aviso('CNPJ inválido: confira os números.'); return; }
        if (/^\d{14}$/.test(documento)) { buscarCnpj(documento); return; }
        // CNPJ alfanumérico: a Receita ainda não tem consulta pública; digita o nome.
        cadastro.documento = documento;
        mostrarCampoNome(true, '');
        atualizarContinuar();
        return;
    }

    if (documento.length === 11 && cadastro.perfil === PERFIL_CONTADOR) {
        if (!cpfValido(documento)) { aviso('CPF inválido: confira os números.'); return; }
        campo.value = formatarDocumento(documento);
        cadastro.documento = documento;
        mostrarCampoNome(true, '');
        document.getElementById('nome').focus();
        atualizarContinuar();
    }
}

// ---------- Criar a conta ----------

async function criarConta(e) {
    e.preventDefault();
    esconderErro();

    const email = document.getElementById('email').value.trim();
    const senha = document.getElementById('senha').value;
    const confirmar = document.getElementById('confirmarSenha').value;
    if (!/^\S+@\S+\.\S+$/.test(email)) return mostrarErroCadastro('Digite um e-mail válido.');
    if (senha.length < 8) return mostrarErroCadastro('A senha precisa ter pelo menos 8 caracteres.');
    if (senha !== confirmar) return mostrarErroCadastro('As duas senhas não estão iguais.');
    if (!document.getElementById('aceitouTermos').checked)
        return mostrarErroCadastro('Para criar a conta, marque que leu e aceita os Termos de uso e a Política de privacidade.');

    const botao = document.getElementById('btn-criar-conta');
    botao.disabled = true;
    botao.innerHTML = '<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span> Criando a conta...';

    try {
        const parametros = new URLSearchParams(window.location.search);
        const resposta = await fetch('/api/auth/registrar', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', ...obterCsrfHeader() },
            body: JSON.stringify({
                perfil: cadastro.perfil,
                documento: cadastro.documento,
                nome: cadastro.nome,
                email,
                senha,
                aceitouTermos: true,
                // Link de indicação (?ref=) e canal (?origem=) que trouxeram a pessoa — ver LinkCadastroExtensions.
                codigoIndicacao: parametros.get('ref'),
                origem: parametros.get('origem')
            })
        });

        if (!resposta.ok) {
            const corpo = await resposta.json().catch(() => null);
            mostrarErroCadastro(extrairMensagemDeErro(corpo) ?? 'Não foi possível criar a conta.');
            return;
        }

        window.location.href = '/';
    } catch {
        mostrarErroCadastro('Não foi possível criar a conta. Tente novamente.');
    } finally {
        botao.disabled = false;
        botao.textContent = 'Criar conta';
    }
}

// ---------- Ligações ----------

document.querySelectorAll('.btn-perfil').forEach(b => b.addEventListener('click', () => escolherPerfil(Number(b.dataset.perfil))));
document.querySelectorAll('[data-voltar]').forEach(b => b.addEventListener('click', () => irParaEtapa(Number(b.dataset.voltar))));
document.getElementById('documento').addEventListener('input', aoDigitarDocumento);
document.getElementById('nome').addEventListener('input', () => {
    if (!cadastro.encontrada) cadastro.nome = document.getElementById('nome').value.trim();
    atualizarContinuar();
});
document.getElementById('btn-continuar-documento').addEventListener('click', () => {
    if (!cadastro.encontrada) cadastro.nome = document.getElementById('nome').value.trim();
    irParaEtapa(3);
});
document.getElementById('form-registrar').addEventListener('submit', criarConta);
