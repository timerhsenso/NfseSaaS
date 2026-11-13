// PL1: controle de valor de um recurso (liga/desliga ou limite), usado na
// grade de recursos do plano (plataforma-planos.js) e nas exceções por conta
// (plataforma.js). TipoRecurso: 1 liga/desliga, 2 limite. OrigemRecurso:
// 1 padrão, 2 plano, 3 exceção.
const TIPO_RECURSO_LIGA = 1;
const TIPO_RECURSO_LIMITE = 2;
const ORIGENS_RECURSO = {
    1: { texto: 'Padrão', cor: 'secondary' },
    2: { texto: 'Plano', cor: 'primary' },
    3: { texto: 'Exceção', cor: 'warning' }
};

function textoValorRecurso(tipo, ligado, limite) {
    if (tipo === TIPO_RECURSO_LIGA) return ligado ? 'Liberado' : 'Bloqueado';
    return limite == null ? 'Ilimitado' : `Até ${Number(limite).toLocaleString('pt-BR')}`;
}

/**
 * Select (+ número para limite). comPadrao: primeira opção "Padrão (…)".
 * valor: { padrao, ligado, limite }.
 */
function controleValorRecurso(prefixo, tipo, valor, comPadrao, textoPadrao) {
    const opcao = (v, texto, marcado) => `<option value="${v}" ${marcado ? 'selected' : ''}>${escaparHtml(texto)}</option>`;
    let modo;
    if (comPadrao && valor.padrao) modo = 'padrao';
    else if (tipo === TIPO_RECURSO_LIGA) modo = valor.ligado === false ? 'bloqueado' : 'liberado';
    else modo = valor.limite == null ? 'ilimitado' : 'limite';

    const opcoes = [
        comPadrao ? opcao('padrao', `Padrão (${textoPadrao})`, modo === 'padrao') : '',
        ...(tipo === TIPO_RECURSO_LIGA
            ? [opcao('liberado', 'Liberado', modo === 'liberado'), opcao('bloqueado', 'Bloqueado', modo === 'bloqueado')]
            : [opcao('ilimitado', 'Ilimitado', modo === 'ilimitado'), opcao('limite', 'Até…', modo === 'limite')])
    ].join('');

    const numero = tipo === TIPO_RECURSO_LIMITE
        ? `<input type="number" class="form-control form-control-sm mt-1 ${modo === 'limite' ? '' : 'd-none'}" id="${prefixo}-limite"
                  min="0" step="1" value="${valor.limite ?? ''}" placeholder="Quantidade" aria-label="Limite" />`
        : '';
    return `<select class="form-select form-select-sm seletor-valor-recurso" id="${prefixo}-modo" data-prefixo="${prefixo}">${opcoes}</select>${numero}`;
}

/** Mostra o número só em "Até…". Chame uma vez por container. */
function ligarControlesRecurso(container) {
    container.addEventListener('change', function (e) {
        const select = e.target.closest('.seletor-valor-recurso');
        if (!select) return;
        const numero = document.getElementById(`${select.dataset.prefixo}-limite`);
        if (!numero) return;
        numero.classList.toggle('d-none', select.value !== 'limite');
        if (select.value === 'limite') numero.focus();
    });
}

/** { padrao, ligado, limite } a partir do controle; erro = mensagem para a tela. */
function lerValorRecurso(prefixo, tipo, nome) {
    const modo = document.getElementById(`${prefixo}-modo`).value;
    if (modo === 'padrao') return { padrao: true, ligado: null, limite: null };
    if (tipo === TIPO_RECURSO_LIGA) return { padrao: false, ligado: modo === 'liberado', limite: null };
    if (modo === 'ilimitado') return { padrao: false, ligado: null, limite: null };
    const texto = document.getElementById(`${prefixo}-limite`).value;
    const limite = Number(texto);
    if (texto === '' || !Number.isInteger(limite) || limite < 0)
        throw new Error(`Informe o limite de "${nome}" (número inteiro, 0 ou mais).`);
    return { padrao: false, ligado: null, limite };
}
