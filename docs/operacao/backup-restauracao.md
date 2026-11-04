# Backup e restauração (produção)

Backup diário externo da VPS de produção para o OneDrive, criptografado pelo
rclone. Instalado e ensaiado em 04/11/2026: o banco restaurado ficou idêntico
ao de produção.

> **Nenhuma senha neste repositório.** As senhas do cofre (`onedrive-crypt`),
> da VPS, do banco e do SMTP ficam no gerenciador de senhas, com uma cópia
> offline. Nunca no Git.

## O que entra no backup

Cada execução gera um `nfsesaas-AAAA-MM-DD_HHMM.tar` (horário UTC) com:

| Arquivo | Origem | Observação |
|---|---|---|
| `banco.dump` | `pg_dump -Fc` do `nfse_saas` (container `nfsesaas-postgres`) | validado com `pg_restore -l` antes do envio |
| `nfsesaas_certificados.tar.gz` | volume `nfsesaas_certificados` | certificados (`*.cert.json`) **e** chaves do Data Protection (`dp-keys/`) |
| `nfsesaas_documentos.tar.gz` | volume `nfsesaas_documentos` | documentos dos contratos |
| `nfsesaas_dataprotection.tar.gz` | volume `nfsesaas_dataprotection` | hoje vazio; guardado por segurança |
| `config.tar.gz` | `docker-compose.prod.yml`, `.env` (se existir), `/etc/nginx`, `/etc/letsencrypt` | o compose de produção contém as senhas: por isso o cofre é criptografado |
| `versao.txt` | `git rev-parse` em `/opt/nfsesaas/NfseSaaS` | commit em produção no momento do backup |
| `SHA256SUMS` | — | conferir com `sha256sum -c SHA256SUMS` |

Sem as chaves do Data Protection, os certificados salvos **não abrem**. Por
isso os dois sempre viajam juntos.

## Onde fica

- Remote `onedrive-crypt:` aponta para `onedrive:Backups/nfsesaas` (OneDrive
  pessoal). Nomes e conteúdo são criptografados.
- Retenção:
  - `diario/`: guarda os últimos **7**.
  - `semanal/`: recebe a cópia de domingo e guarda os últimos **4**. A
    primeira cópia vai também quando a pasta está vazia.
  - Cópia local em `/var/backups/nfsesaas` (permissão 700): guarda os últimos **3**.
- Agendamento: todo dia às **06:30 UTC (03:30 em Brasília)**.
- Log: `/var/log/nfsesaas-backup.log` (logrotate semanal, 8 semanas).

## Arquivos neste repositório

| Repositório | Na VPS |
|---|---|
| `deploy/backup/backup.sh` | `/opt/nfsesaas/backup/backup.sh` (cópia, permissão 700) |
| `deploy/backup/nfsesaas-backup.cron` | `/etc/cron.d/nfsesaas-backup` |
| `deploy/backup/nfsesaas-backup.logrotate` | `/etc/logrotate.d/nfsesaas-backup` |

O cron aponta para a **cópia** em `/opt/nfsesaas/backup`, e não para o clone
do repositório. Assim, um `git pull` com problema não derruba o backup.
Depois de alterar o script no repositório, reinstale na VPS:

```bash
cd /opt/nfsesaas/NfseSaaS && git pull
install -m 700 deploy/backup/backup.sh /opt/nfsesaas/backup/backup.sh
install -m 644 deploy/backup/nfsesaas-backup.cron /etc/cron.d/nfsesaas-backup
install -m 644 deploy/backup/nfsesaas-backup.logrotate /etc/logrotate.d/nfsesaas-backup
/opt/nfsesaas/backup/backup.sh   # roda um backup para conferir
```

## Pré-requisitos (instalação inicial, já feita em 04/11/2026)

1. rclone na VPS: `curl https://rclone.org/install.sh | bash`.
2. Remote `onedrive`: rodar `rclone authorize "onedrive"` no Windows e
   colar o token no `rclone config` da VPS (sem navegador; region `global`;
   config_type `onedrive`; primeiro drive).
3. Remote `onedrive-crypt` com duas senhas aleatórias (guardadas no
   gerenciador de senhas):

   ```bash
   P1=$(openssl rand -base64 24); P2=$(openssl rand -base64 24)
   rclone config create onedrive-crypt crypt remote=onedrive:Backups/nfsesaas \
     password="$P1" password2="$P2" \
     filename_encryption=standard directory_name_encryption=true --obscure
   echo "SENHA 1: $P1"; echo "SENHA 2: $P2"
   ```

## Operação do dia a dia

```bash
tail -20 /var/log/nfsesaas-backup.log     # último resultado (procure "SUCESSO")
/opt/nfsesaas/backup/backup.sh            # backup manual (sempre antes de deploy/migration)
rclone ls onedrive-crypt:                 # o que está no OneDrive
```

## Ensaio de restauração (mensal, sem tocar na produção)

Baixa o último backup diário, confere o SHA256, restaura num Postgres
temporário (`restore-teste`), compara as contagens com a produção e apaga
tudo no final.

```bash
R=/root/teste-restauracao; rm -rf $R; mkdir -p $R && cd $R
ARQ=$(rclone lsf onedrive-crypt:diario/ | sort | tail -1); echo ">>> Baixando do OneDrive: $ARQ"
rclone copy "onedrive-crypt:diario/$ARQ" .
tar -xf "$ARQ" && echo ">>> Conferindo integridade:" && sha256sum -c SHA256SUMS
echo ">>> Versao:"; cat versao.txt
echo ">>> Conteudo de certificados/chaves:"; tar -tzf nfsesaas_certificados.tar.gz
docker rm -f restore-teste >/dev/null 2>&1
docker run -d --name restore-teste -e POSTGRES_USER=nfse -e POSTGRES_PASSWORD=teste -e POSTGRES_DB=nfse_saas postgres:17 >/dev/null
for i in $(seq 1 60); do [ "$(docker logs restore-teste 2>&1 | grep -c 'ready to accept connections')" -ge 2 ] && break; sleep 2; done
docker exec -i restore-teste pg_restore -U nfse -d nfse_saas --no-owner --exit-on-error < banco.dump && echo "    restauracao OK"
Q="select 'tabelas', count(*) from information_schema.tables where table_schema not in ('pg_catalog','information_schema') union all select 'contas', count(*) from tenants union all select 'empresas', count(*) from empresas union all select 'usuarios', count(*) from \"AspNetUsers\" union all select 'notas', count(*) from notas_fiscais union all select 'contratos', count(*) from contratos;"
echo ">>> RESTAURADO:"; docker exec restore-teste psql -U nfse -d nfse_saas -At -F ' = ' -c "$Q"
echo ">>> PRODUCAO:";   docker exec nfsesaas-postgres psql -U nfse -d nfse_saas -At -F ' = ' -c "$Q"
docker rm -f restore-teste >/dev/null; cd /; rm -rf $R; echo ">>> FIM DO ENSAIO"
```

Está ok quando todos os arquivos dão `OK`, aparece `restauracao OK` e as
contagens de RESTAURADO e PRODUCAO batem. Uma diferença pequena em "notas"
é normal se houve emissão depois do backup.

## Restauração real (perda da VPS)

Para recuperar do zero, só são necessários o **login da conta Microsoft** e
a **SENHA 1** e a **SENHA 2** do cofre.

1. VPS nova (Ubuntu) com Docker e Nginx. Instalar o rclone.
2. Recriar o `onedrive` (`rclone authorize` + `rclone config`). Recriar o
   `onedrive-crypt` com **as mesmas** SENHA 1 e SENHA 2:
   `rclone config create onedrive-crypt crypt remote=onedrive:Backups/nfsesaas password=<SENHA1> password2=<SENHA2> filename_encryption=standard directory_name_encryption=true --obscure`.
3. Baixar o backup mais recente (`rclone copy onedrive-crypt:diario/<arquivo> .`),
   extrair e rodar `sha256sum -c SHA256SUMS`.
4. `git clone` do repositório em `/opt/nfsesaas/NfseSaaS` e `git checkout`
   do commit de `versao.txt`. Extrair `config.tar.gz` com `tar -xzf config.tar.gz -C /`:
   isso restaura o compose, o nginx e o letsencrypt.
5. Subir só o banco: `docker compose -f docker-compose.prod.yml up -d postgres`.
   Depois restaurar:
   `docker exec -i nfsesaas-postgres pg_restore -U nfse -d nfse_saas --no-owner --clean --if-exists < banco.dump`.
6. Criar os volumes da aplicação (`docker compose -f docker-compose.prod.yml create app`)
   e, para cada `nfsesaas_*.tar.gz`, extrair no Mountpoint do volume:
   `tar -xzf nfsesaas_certificados.tar.gz -C "$(docker volume inspect -f '{{ .Mountpoint }}' nfsesaas_certificados)"`.
7. `docker compose -f docker-compose.prod.yml up -d --build app`. No log,
   conferir a linha "Chaves do Data Protection…". Na tela de Empresa, usar
   **Testar conexão** do certificado (prova que chaves e certificado abriram).
8. Reinstalar o backup (seção "Arquivos neste repositório") e rodar um
   backup manual.

## Pendências conhecidas

- As chaves do Data Protection estão em disco **sem criptografia**
  (`/app/data/certificados/dp-keys`). Configurar
  `DataProtection__CertificadoPfxPath`/`CertificadoSenha`. Esse .pfx e a
  senha dele passam a ser item obrigatório do gerenciador de senhas: sem
  eles, as chaves não abrem.
- Alerta por e-mail quando o backup falhar: previsto no item de
  Monitoramento da Fase 1.
