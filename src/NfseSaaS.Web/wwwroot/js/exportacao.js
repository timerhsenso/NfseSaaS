/**
 * Adiciona botões de Copiar/CSV/Excel/Imprimir a um DataTable já
 * inicializado, seguindo 2 exigências:
 *
 * 1. Toda exportação/impressão sai com um rodapé mostrando quem
 *    exportou e quando (data/hora do NAVEGADOR do usuário, no momento
 *    do clique — não precisa bater no servidor pra isso).
 * 2. Toda exportação/impressão é registrada no AuditLog do backend
 *    (POST /api/exportacoes) — é só o METADADO (tela + formato), nunca
 *    o conteúdo da grid; ver ExportacoesController.
 *
 * Uso: depois de `new DataTable('#minhaTabela', {...})`, chama
 * `configurarExportacao(tabela, 'Empresas')`.
 */
function configurarExportacao(tabela, nomeTela) {
    const linhaRodape = () => `Exportado por ${window.usuarioAtual ?? 'desconhecido'} em ${new Date().toLocaleString('pt-BR')}`;
    const nomeArquivo = () => `${nomeTela}_${new Date().toISOString().slice(0, 10)}`;

    new $.fn.dataTable.Buttons(tabela, {
        buttons: [
            {
                extend: 'copyHtml5',
                text: '<i class="bi bi-clipboard"></i> Copiar'
            },
            {
                extend: 'csvHtml5',
                text: '<i class="bi bi-filetype-csv"></i> CSV',
                filename: nomeArquivo,
                customizeData: function (data) {
                    data.body.push([linhaRodape()]);
                }
            },
            {
                extend: 'excelHtml5',
                text: '<i class="bi bi-file-earmark-excel"></i> Excel',
                filename: nomeArquivo,
                customizeData: function (data) {
                    data.body.push([linhaRodape()]);
                }
            },
            {
                extend: 'print',
                text: '<i class="bi bi-printer"></i> Imprimir',
                title: nomeTela,
                customize: function (win) {
                    $(win.document.body).append(`<div class="rodape-exportacao">${linhaRodape()}</div>`);
                }
            }
        ]
    });

    // Container padrão dos botões — a própria extensão gera um <div
    // class="dt-buttons">; só precisamos colocá-lo em algum lugar
    // visível da tela. O elemento com id "botoes-exportacao-<tabela>"
    // deve existir na view (uma div vazia no card-header, por
    // convenção) — se não existir, os botões ficam soltos no início
    // do body (comportamento padrão da extensão), então prefira sempre
    // criar o placeholder na view.
    var containerId = 'botoes-exportacao-' + tabela.table().node().id;
    var container = document.getElementById(containerId);
    if (container) {
        container.appendChild(tabela.buttons().container()[0]);
    }

    // Registro de auditoria — nunca bloqueia a exportação em si: se o
    // log falhar (rede etc.), o usuário ainda consegue exportar/imprimir.
    tabela.on('buttons-action.dt', function (e, buttonApi, dt, node, config) {
        var formato = (config && config.extend) ? config.extend : 'desconhecido';
        fetch('/api/exportacoes', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ tela: nomeTela, formato: formato })
        }).catch(function (err) {
            console.warn('Não foi possível registrar a exportação:', err);
        });
    });
}
