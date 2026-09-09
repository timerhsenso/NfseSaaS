/**
 * Clientes, Serviços e Nfse são todos escopados por Empresa na API
 * (um Tenant pode ter várias Empresas emissoras). Este helper povoa um
 * <select id="seletor-empresa"> com as Empresas do Tenant e lembra a
 * última escolhida em sessionStorage, pra não pedir de novo a cada
 * troca de tela dentro da mesma sessão do navegador.
 */
async function inicializarSeletorEmpresa(aoTrocarEmpresa) {
    const select = document.getElementById('seletor-empresa');

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

        const salvaId = sessionStorage.getItem('empresaSelecionadaId');
        const existeNaLista = salvaId && resultado.items.some(e => e.id === salvaId);
        select.value = existeNaLista ? salvaId : resultado.items[0].id;

        select.addEventListener('change', function () {
            sessionStorage.setItem('empresaSelecionadaId', select.value);
            aoTrocarEmpresa(select.value);
        });

        aoTrocarEmpresa(select.value);
    } catch (err) {
        mostrarErro(err.message);
    }
}
