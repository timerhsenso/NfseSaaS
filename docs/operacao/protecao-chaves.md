# Proteção das chaves do Data Protection (produção)

As chaves do ASP.NET Core Data Protection ficam em
`/app/data/certificados/dp-keys`, no volume `nfsesaas_certificados`. Elas
abrem os certificados A1 de **todos** os clientes e assinam o cookie de
login. Em texto aberto, quem tiver uma cópia do volume ou do backup consegue
abrir os certificados e forjar um login.

A proteção funciona assim: um certificado próprio do sistema (não é o A1 de
nenhum cliente) criptografa as chaves em disco.

| Item | Onde fica | No Git | No backup diário |
|---|---|---|---|
| `dp-protecao.pfx` (certificado e chave privada, com senha) | `/opt/nfsesaas/segredos/` (pasta 700) | não | não |
| `docker-compose.segredos.yml` (monta o .pfx e passa a senha ao app) | `/opt/nfsesaas/NfseSaaS/` (600) | não (`.gitignore`) | não |
| Senha e conteúdo do .pfx | gerenciador de senhas, item "micoempresa - certificado de proteção das chaves" | não | não |

O backup leva as chaves criptografadas, e o gerenciador de senhas guarda o
que abre essas chaves. Um não serve sem o outro.

> **Nunca apague nem gere de novo o `dp-protecao.pfx` depois de ativado.**
> Sem ele, as chaves não abrem e os certificados de todos os clientes viram
> lixo. O script se recusa a sobrescrever.

## Ativação (uma vez)

1. Backup manual: `/opt/nfsesaas/backup/backup.sh`.
2. `cd /opt/nfsesaas/NfseSaaS && git pull && bash deploy/protecao-chaves/configurar.sh`.
   O script mostra a SENHA e o ARQUIVO (texto entre INICIO e FIM). Guarde
   os dois no gerenciador de senhas antes de seguir.
3. GitHub › Actions › **Deploy Production**. O workflow encontra o
   `docker-compose.segredos.yml` e passa a usá-lo em todos os comandos.
4. Em **Plataforma › Logs**, na subida, deve aparecer "protegidas por
   certificado {thumbprint}" (o mesmo thumbprint que o script mostrou) e o
   aviso de chaves antigas sem criptografia.
5. Em **Plataforma › Contas** (fim da página), cartão **Proteção das chaves**: clique em
   **Proteger chaves antigas**. O sistema faz o seguinte:
   1. cria uma chave nova criptografada;
   2. regrava todos os certificados com ela;
   3. confere que nenhum certificado depende da chave antiga;
   4. só então apaga as chaves antigas sem criptografia.

   Se algo falhar, nada é apagado.
6. Confira o certificado na tela **Empresas** (Testar conexão) e rode de
   novo um backup manual.

Efeito esperado: na próxima vez que o sistema reiniciar (deploy), todos
precisam entrar de novo, e links antigos de "esqueci a senha" deixam de
valer. Os backups antigos, de antes da proteção, ainda têm a chave aberta e
somem sozinhos pela retenção (até 4 semanas).

## Estado em produção

Ativada em 04/11/2026: chave antiga apagada, certificados regravados,
Testar conexão e backup conferidos.

## Restauração (VPS nova)

Antes de subir o app (passo 7 do backup-restauracao.md):

```bash
cd /opt/nfsesaas/NfseSaaS
bash deploy/protecao-chaves/configurar.sh --restaurar
```

O script pede a SENHA e o ARQUIVO do gerenciador de senhas, confere se o
.pfx abre e recria os dois arquivos. Depois disso, o compose sobe com
`-f docker-compose.prod.yml -f docker-compose.segredos.yml`.

## Validade

O certificado vale 10 anos. Vencido, ele continua abrindo as chaves: o Data
Protection não confere a validade. Trocar o certificado é um procedimento à
parte (os dois certificados ficam configurados durante a troca). Planeje com
antecedência.
