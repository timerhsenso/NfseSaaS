#!/usr/bin/env python3
"""
Gera src/NfseSaaS.Application/Rejeicoes/rejeicoes-sefin.json a partir das
planilhas OFICIAIS de leiaute e regras de negócio da NFS-e Nacional
(gov.br/nfse › Biblioteca › Documentação técnica):

  Anexo I  – DPS/NFS-e        (anexo_i-sefin_adn-dps_nfse-...xlsx)
  Anexo II – Eventos           (anexo_ii-sefin_adn-pedregevt_evt-...xlsx)
  Anexo VI – IBS/CBS (RTC)     (AnexoVI-LeiautesRN_RTC_IBSCBS-...xlsx)

Uso (na raiz do repositório):
  pip install openpyxl
  python3 tools/rejeicoes-sefin/gerar_catalogo.py PLANILHA1.xlsx PLANILHA2.xlsx ...

Passe as planilhas da mais importante para a menos importante (produção
antes de produção restrita): quando o mesmo código aparece em mais de uma,
vale a mensagem da PRIMEIRA. Lê toda aba que tenha as colunas "CÓD. ERRO" e
"MSG. ERRO"; nas abas de regras, o caminho/campo XML de cada linha é
herdado da última linha que o informou (as planilhas mesclam células).
"""
import json
import os
import re
import sys

import openpyxl

SAIDA = os.path.join('src', 'NfseSaaS.Application', 'Rejeicoes', 'rejeicoes-sefin.json')
CODIGO = re.compile(r'^E\d{3,4}$')


def limpar(valor):
    return re.sub(r'\s+', ' ', str(valor)).strip() if valor is not None else None


def ler(caminho_planilha, catalogo):
    # Tira prefixo de upload/download (ex.: "85ca530c-") para ficar só o nome oficial.
    fonte = re.sub(r'^[0-9a-f]{8}-', '', os.path.basename(caminho_planilha))
    planilha = openpyxl.load_workbook(caminho_planilha, read_only=True, data_only=True)
    for aba in planilha.worksheets:
        linhas = list(aba.iter_rows(values_only=True))
        cabecalho = None
        for i, linha in enumerate(linhas[:6]):
            valores = [limpar(c) or '' for c in linha]
            if 'CÓD. ERRO' in valores and 'MSG. ERRO' in valores:
                cabecalho = (i, valores.index('CÓD. ERRO'), valores.index('MSG. ERRO'))
                break
        if cabecalho is None:
            continue

        inicio, col_codigo, col_mensagem = cabecalho
        recepcao = 'RECEP' in aba.title.upper()
        caminho = campo = None
        for linha in linhas[inicio + 1:]:
            linha = list(linha) + [None] * 20
            if not recepcao:
                if linha[1] not in (None, ''):
                    caminho = limpar(linha[1])
                if linha[2] not in (None, ''):
                    campo = limpar(linha[2])
            texto_codigo = limpar(linha[col_codigo])
            if not texto_codigo:
                continue
            for codigo in re.split(r'[\s/,;]+', texto_codigo):
                if CODIGO.match(codigo) and codigo not in catalogo:
                    catalogo[codigo] = {
                        'codigo': codigo,
                        'mensagem': limpar(linha[col_mensagem]),
                        'campo': None if recepcao else campo,
                        'caminho': 'RECEPCAO' if recepcao else caminho,
                        'fonte': fonte,
                    }


def main(planilhas):
    if not planilhas:
        sys.exit(__doc__)
    catalogo = {}
    for planilha in planilhas:
        ler(planilha, catalogo)
    itens = [catalogo[c] for c in sorted(catalogo)]
    with open(SAIDA, 'w', encoding='utf-8', newline='\n') as arquivo:
        json.dump(itens, arquivo, ensure_ascii=False, indent=1)
        arquivo.write('\n')
    print(f'{len(itens)} códigos gravados em {SAIDA}')


if __name__ == '__main__':
    main(sys.argv[1:])
