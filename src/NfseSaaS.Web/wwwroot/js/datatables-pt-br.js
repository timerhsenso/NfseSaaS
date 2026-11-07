// Tradução pt-BR do DataTables como PADRÃO de todas as tabelas.
// Antes cada tela baixava o arquivo da CDN (cdn.datatables.net); se a CDN
// falhasse, a tabela aparecia em inglês ("entries per page"). Agora vem
// junto com o site. Texto oficial do pacote datatables.net-plugins 2.3.6
// (i18n/pt-BR.json), só com as partes que o sistema usa: tabela,
// paginação, busca, botões de exportação e seleção.
// Carregado depois do DataTables e antes das telas (ver _Layout).
(function () {
    if (!window.DataTable) return;
    DataTable.defaults.language = {
        "aria": {
            "orderable": "Ordenar",
            "orderableRemove": "Remover ordenação",
            "orderableReverse": "Inverter a ordenação",
            "paginate": {
                "first": "Primeiro",
                "last": "Último",
                "next": "Próximo",
                "previous": "Anterior"
            }
        },
        "buttons": {
            "collection": "Coleção  <span class=\"ui-button-icon-primary ui-icon ui-icon-triangle-1-s\"></span>",
            "colvis": "Visibilidade da Coluna",
            "colvisRestore": "Restaurar Visibilidade",
            "copy": "Copiar",
            "copyKeys": "Pressione ctrl ou u2318 + C para copiar os dados da tabela para a área de transferência do sistema. Para cancelar, clique nesta mensagem ou pressione Esc..",
            "copySuccess": {
                "_": "%d linhas copiadas com sucesso",
                "1": "Uma linha copiada com sucesso"
            },
            "copyTitle": "Copiar para a Área de Transferência",
            "createState": "Criar estado",
            "csv": "CSV",
            "excel": "Excel",
            "pageLength": {
                "_": "Mostrar %d registros",
                "-1": "Mostrar todos os registros",
                "1": "Mostrar 1 registro"
            },
            "pdf": "PDF",
            "print": "Imprimir",
            "removeAllStates": "Remover todos os estados",
            "removeState": "Remover",
            "renameState": "Renomear",
            "savedStates": "Estados salvos",
            "stateRestore": "Estado %d",
            "updateState": "Atualizar"
        },
        "decimal": "",
        "emptyTable": "Nenhum registro encontrado",
        "info": "Mostrando de _START_ até _END_ de _TOTAL_ registros",
        "infoEmpty": "Mostrando 0 até 0 de 0 registro(s)",
        "infoFiltered": "(Filtrados de _MAX_ registros)",
        "infoPostFix": "",
        "infoThousands": ".",
        "lengthLabels": {
            "-1": "Todos"
        },
        "lengthMenu": "Exibir _MENU_ resultados por página",
        "loadingRecords": "Carregando...",
        "orderClear": "Limpar ordenação",
        "processing": "Carregando...",
        "search": "Pesquisar",
        "searchPlaceholder": "",
        "select": {
            "cells": {
                "_": "%d células selecionadas",
                "0": "",
                "1": "1 célula selecionada"
            },
            "columns": {
                "_": "%d colunas selecionadas",
                "0": "",
                "1": "1 coluna selecionada"
            },
            "rows": {
                "_": "Selecionado %d linhas",
                "0": "",
                "1": "Selecionado 1 linha"
            }
        },
        "thousands": ".",
        "zeroRecords": "Nenhum registro encontrado"
    };
})();
