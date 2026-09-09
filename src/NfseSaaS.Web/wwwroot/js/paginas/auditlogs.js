let paginaAtualAuditoria = 1;
const tamanhoPaginaAuditoria = 20;

document.addEventListener('DOMContentLoaded', function () {
    document.getElementById('form-filtros-auditoria').addEventListener('submit', function (e) {
        e.preventDefault();
        paginaAtualAuditoria = 1;
        carregarAuditoria();
    });

    document.getElementById('btn-limpar-filtros-auditoria').addEventListener('click', function () {
        document.getElementById('form-filtros-auditoria').reset();
        paginaAtualAuditoria = 1;
        carregarAuditoria();
    });

    document.getElementById('btn-pagina-anterior-auditoria').addEventListener('click', function () {
        if (paginaAtualAuditoria > 1) {
            paginaAtualAuditoria--;
            carregarAuditoria();
        }
    });

    document.getElementById('btn-proxima-pagina-auditoria').addEventListener('click', function () {
        paginaAtualAuditoria++;
        carregarAuditoria();
    });

    carregarAuditoria();
});

async function carregarAuditoria() {
    const parametros = new URLSearchParams({
        page: paginaAtualAuditoria,
        pageSize: tamanhoPaginaAuditoria
    });

    const operacao = document.getElementById('filtro-operacao').value.trim();
    const entidade = document.getElementById('filtro-entidade').value.trim();
    const dataInicio = document.getElementById('filtro-data-inicio').value;
    const dataFim = document.getElementById('filtro-data-fim').value;

    if (operacao) parametros.set('operacao', operacao);
    if (entidade) parametros.set('entidade', entidade);
    if (dataInicio) parametros.set('dataInicio', dataInicio);
    if (dataFim) parametros.set('dataFim', dataFim);

    try {
        const resultado = await apiFetch(`/api/auditlogs?${parametros.toString()}`);

        const corpo = document.getElementById('corpo-tabela-auditoria');
        corpo.innerHTML = resultado.items.length
            ? resultado.items.map(log => `
                <tr>
                    <td>${new Date(log.dataHora).toLocaleString('pt-BR')}</td>
                    <td>${log.operacao}</td>
                    <td>${log.entidade}</td>
                    <td><small>${log.entidadeId ?? '—'}</small></td>
                    <td>${log.ipAddress ?? '—'}</td>
                    <td><small class="text-muted">${log.dados ?? '—'}</small></td>
                </tr>
            `).join('')
            : '<tr><td colspan="6" class="text-center text-muted">Nenhum registro encontrado.</td></tr>';

        const totalPaginas = Math.max(1, Math.ceil(resultado.totalCount / tamanhoPaginaAuditoria));
        document.getElementById('resumo-paginacao-auditoria').textContent =
            `Página ${resultado.page} de ${totalPaginas} — ${resultado.totalCount} registro(s)`;

        document.getElementById('btn-pagina-anterior-auditoria').disabled = paginaAtualAuditoria <= 1;
        document.getElementById('btn-proxima-pagina-auditoria').disabled = paginaAtualAuditoria >= totalPaginas;
    } catch (err) {
        mostrarErro(err.message);
    }
}
