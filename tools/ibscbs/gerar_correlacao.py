#!/usr/bin/env python3
"""
Gera src/NfseSaaS.Application/Fiscal/IbsCbs/correlacao-ibscbs.json a partir
das planilhas OFICIAIS da Reforma Tributária na NFS-e
(gov.br/nfse › Biblioteca › Documentação técnica › RTC):

  Anexo VII  – Indicador da operação (cIndOp)
               anexovii-indop_ibscbs_v1-03-00-nt009.xlsx
  Anexo VIII – Correlação Item LC 116 / NBS / cIndOp / cClassTrib
               anexo_viii-tabela-correlacao-v01-02-00-20261026.xlsx

Uso (na raiz do repositório):
  pip install openpyxl
  python3 tools/ibscbs/gerar_correlacao.py ANEXO_VII.xlsx ANEXO_VIII.xlsx

O Anexo VIII vem com células mescladas: cada item da LC 116 ocupa várias
linhas, e as colunas NBS, cIndOp, cClassTrib e atividade do Simples listam,
cada uma, as OPÇÕES admitidas para o item (não são combinações linha a
linha). O script junta, por item, a lista de cada coluna, na ordem da
planilha (a primeira opção vira a sugestão padrão no cadastro do Serviço).
"""
import json
import os
import re
import sys

import openpyxl

SAIDA = os.path.join('src', 'NfseSaaS.Application', 'Fiscal', 'IbsCbs', 'correlacao-ibscbs.json')


def limpar(valor):
    return re.sub(r'\s+', ' ', str(valor)).strip() if valor is not None else ''


def so_digitos(valor):
    return re.sub(r'\D', '', limpar(valor))


def versao(caminho, padrao):
    m = re.search(padrao, os.path.basename(caminho), re.I)
    return f'{int(m.group(1))}.{m.group(2)}.{m.group(3)}' if m else ''


def ler_indop(caminho):
    planilha = openpyxl.load_workbook(caminho, read_only=True, data_only=True)
    linhas = list(planilha.worksheets[0].iter_rows(values_only=True))
    cab = [limpar(c) for c in linhas[0]]
    col = {nome: cab.index(nome) for nome in cab if nome}
    itens = []
    for linha in linhas[1:]:
        codigo = so_digitos(linha[col['Código indOp']])
        if len(codigo) != 6:
            continue
        itens.append({
            'codigo': codigo,
            'tipo': limpar(linha[col['Tipo de operação']]),
            'caracteristica': limpar(linha[col['Característica do fornecimento']]),
            'local': limpar(linha[col['NFSeLocIncidIBS']]),
            'nfse': limpar(linha[col['indNFSe']]).upper() == 'S',
        })
    return itens


def ler_correlacao(caminho):
    planilha = openpyxl.load_workbook(caminho, read_only=True, data_only=True)
    linhas = list(planilha['tabela geral'].iter_rows(values_only=True))
    itens, nomes_class = {}, {}
    atual = None
    for linha in linhas[1:]:
        linha = [limpar(c) for c in list(linha) + [None] * 12]
        if linha[0]:
            chave = so_digitos(linha[0])
            atual = itens.setdefault(chave, {
                'item': chave, 'descricao': linha[1], 'nbs': [], 'indOps': [], 'cClassTribs': [], 'atividadesSn': []})
        if atual is None:
            continue
        for valor, lista, normalizar in (
                (linha[2], 'nbs', so_digitos),
                (linha[6], 'indOps', so_digitos),
                (linha[8], 'cClassTribs', so_digitos),
                (linha[4], 'atividadesSn', limpar)):
            v = normalizar(valor)
            if v and v not in atual[lista]:
                atual[lista].append(v)
        if linha[8]:
            nomes_class[so_digitos(linha[8])] = linha[9]
    return [itens[k] for k in sorted(itens)], [{'codigo': k, 'nome': nomes_class[k]} for k in sorted(nomes_class)]


def main(argumentos):
    if len(argumentos) != 2:
        sys.exit(__doc__)
    anexo_vii, anexo_viii = argumentos
    itens, classificacoes = ler_correlacao(anexo_viii)
    dados = {
        'versaoAnexoVII': versao(anexo_vii, r'v(\d+)-(\d+)-(\d+)'),
        'versaoAnexoVIII': versao(anexo_viii, r'v(\d+)-(\d+)-(\d+)'),
        'indOps': ler_indop(anexo_vii),
        'cClassTribs': classificacoes,
        'itens': itens,
    }
    with open(SAIDA, 'w', encoding='utf-8', newline='\n') as arquivo:
        json.dump(dados, arquivo, ensure_ascii=False, indent=1)
        arquivo.write('\n')
    print(f"{len(dados['indOps'])} indicadores, {len(classificacoes)} classificações e {len(itens)} itens gravados em {SAIDA}")


if __name__ == '__main__':
    main(sys.argv[1:])
