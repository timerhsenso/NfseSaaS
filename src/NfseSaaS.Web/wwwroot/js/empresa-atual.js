/**
 * Fonte única de qual Empresa está selecionada no momento. Substitui o
 * antigo empresa-seletor.js, que vivia dentro de cada página e guardava
 * a escolha em sessionStorage. Agora o <select> existe uma vez só, no
 * topo (_Layout.cshtml), e a escolha fica num COOKIE — não
 * sessionStorage — porque o Razor precisa conseguir ler o mesmo valor
 * no servidor (pra montar o badge de ambiente sem esperar o JS rodar e
 * sem "piscar" o valor errado por uma fração de segundo).
 *
 * O cookie não é sensível: só lembra qual Empresa está sendo olhada,
 * não concede nenhuma permissão — o back-end continua validando tudo
 * por Tenant/Role do jeito que já fazia, em cada endpoint.
 */
const NOME_COOKIE_EMPRESA_ATUAL = 'EmpresaAtualId';

function obterEmpresaAtualId() {
    const match = document.cookie.match(new RegExp('(?:^|; )' + NOME_COOKIE_EMPRESA_ATUAL + '=([^;]*)'));
    return match ? decodeURIComponent(match[1]) : null;
}

function definirEmpresaAtualId(empresaId) {
    const expira = new Date();
    expira.setDate(expira.getDate() + 365);
    document.cookie = `${NOME_COOKIE_EMPRESA_ATUAL}=${encodeURIComponent(empresaId)}; expires=${expira.toUTCString()}; path=/; SameSite=Lax`;
}

/**
 * Roda em toda página (o <select id="seletor-empresa-topo"> vive no
 * _Layout) — popula a lista de empresas, garante que sempre existe uma
 * selecionada (cookie) e recarrega a página quando a seleção muda ou
 * quando precisa resolver um valor novo (primeira visita, sem cookie
 * ainda; ou a empresa do cookie não existe mais na lista, por exemplo
 * foi excluída). Recarregar é deliberado: é o jeito mais simples de
 * garantir que TODA a página — o badge server-side no topo e os dados
 * carregados via JS mais abaixo — fique consistente com a nova escolha,
 * sem precisar espalhar um mecanismo de "avisar todo mundo que mudou"
 * entre scripts de páginas diferentes.
 */
async function inicializarSeletorEmpresaTopo() {
    const select = document.getElementById('seletor-empresa-topo');
    if (!select) return;

    try {
        const resultado = await apiFetch('/api/empresas?pageSize=200');
        select.innerHTML = '';

        if (!resultado.items.length) {
            select.innerHTML = '<option value="">Nenhuma empresa cadastrada</option>';
            return;
        }

        for (const empresa of resultado.items) {
            const option = document.createElement('option');
            option.value = empresa.id;
            option.textContent = `${empresa.razaoSocial} (${empresa.cnpj})`;
            select.appendChild(option);
        }

        let idAtual = obterEmpresaAtualId();
        const existeNaLista = idAtual && resultado.items.some(e => e.id === idAtual);

        if (!existeNaLista) {
            idAtual = resultado.items[0].id;
            definirEmpresaAtualId(idAtual);
            location.reload();
            return;
        }

        select.value = idAtual;

        select.addEventListener('change', function () {
            definirEmpresaAtualId(select.value);
            location.reload();
        });
    } catch (err) {
        mostrarErro(err.message);
    }
}

document.addEventListener('DOMContentLoaded', inicializarSeletorEmpresaTopo);
