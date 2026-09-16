(async function () {
    try {
        const dados = await apiFetch('/api/sobre');

        document.getElementById('sobre-versao').textContent = dados.versao;
        document.getElementById('sobre-commit').textContent = dados.commit;
        document.getElementById('sobre-deploy-em').textContent = dados.deployEm
            ? new Date(dados.deployEm).toLocaleString('pt-BR')
            : 'não disponível';
        document.getElementById('sobre-changelog').textContent = dados.changelog;
    } catch (erro) {
        document.getElementById('sobre-changelog').textContent = 'Erro ao carregar: ' + erro.message;
    }
})();
