// Página pública /erros-nfse: filtra a lista de códigos enquanto a pessoa digita.
// Ao apertar Enter com um código completo (ex.: E0312), abre a página dele.
document.addEventListener('DOMContentLoaded', function () {
    const busca = document.getElementById('busca-erro');
    if (!busca) return;

    const grupos = Array.from(document.querySelectorAll('[data-grupo]'));
    const vazia = document.getElementById('busca-vazia');

    function normalizar(texto) {
        return (texto || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').trim();
    }

    function filtrar() {
        const termo = normalizar(busca.value);
        let visiveis = 0;

        grupos.forEach(function (grupo) {
            let noGrupo = 0;
            grupo.querySelectorAll('li[data-busca]').forEach(function (item) {
                const mostra = !termo || normalizar(item.dataset.busca).includes(termo);
                item.classList.toggle('d-none', !mostra);
                if (mostra) noGrupo++;
            });
            grupo.classList.toggle('d-none', noGrupo === 0);
            visiveis += noGrupo;
        });

        vazia.classList.toggle('d-none', visiveis > 0);
    }

    busca.addEventListener('input', filtrar);
    busca.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') return;
        const codigo = busca.value.trim().toUpperCase();
        if (/^E\d{4}$/.test(codigo)) {
            e.preventDefault();
            window.location.href = '/erros-nfse/' + codigo;
        }
    });

    // Chegou com ?q=E0312 (ex.: link compartilhado): já aplica o filtro.
    const q = new URLSearchParams(window.location.search).get('q');
    if (q) {
        busca.value = q;
        filtrar();
    }
});
