#!/usr/bin/env bash
# Backup diario do NfseSaaS -> OneDrive (criptografado pelo rclone crypt "onedrive-crypt")
# Instalado na VPS em /opt/nfsesaas/backup/backup.sh (copia deste arquivo; ver docs/operacao/backup-restauracao.md).
# Conteudo: banco (pg_dump), volumes (certificados + chaves dp-keys, documentos, dataprotection),
# configuracao (docker-compose.prod.yml, .env, nginx, letsencrypt) e versao (commit).
set -Eeuo pipefail
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export RCLONE_CONFIG=/root/.config/rclone/rclone.conf

APP_DIR=/opt/nfsesaas/NfseSaaS
LOCAL_DIR=/var/backups/nfsesaas
REMOTO=onedrive-crypt:
PG_CONTAINER=nfsesaas-postgres
PG_USER=nfse
PG_DB=nfse_saas
VOLUMES=(nfsesaas_certificados nfsesaas_documentos nfsesaas_dataprotection)
MANTER_DIARIOS=7
MANTER_SEMANAIS=4
MANTER_LOCAIS=3

log() { echo "$(date '+%F %T') $*"; }

exec 9>/var/lock/nfsesaas-backup.lock
flock -n 9 || { log "Outro backup ainda esta rodando. Saindo."; exit 1; }

TMP=""
trap 'log "ERRO: backup FALHOU na linha $LINENO"; [ -n "$TMP" ] && rm -rf "$TMP"' ERR

mkdir -p "$LOCAL_DIR"; chmod 700 "$LOCAL_DIR"
find "$LOCAL_DIR" -maxdepth 1 -name 'tmp.*' -mmin +120 -exec rm -rf {} + 2>/dev/null || true

CARIMBO=$(date -u '+%Y-%m-%d_%H%M')
NOME="nfsesaas-${CARIMBO}.tar"
TMP=$(mktemp -d "$LOCAL_DIR/tmp.XXXX")

log "===== Inicio do backup $NOME ====="

log "1/5 Banco de dados"
docker exec "$PG_CONTAINER" pg_dump -U "$PG_USER" -d "$PG_DB" -Fc > "$TMP/banco.dump"
docker exec -i "$PG_CONTAINER" pg_restore -l < "$TMP/banco.dump" > /dev/null
log "    banco ok ($(du -h "$TMP/banco.dump" | cut -f1))"

log "2/5 Arquivos (certificados, chaves, documentos)"
for v in "${VOLUMES[@]}"; do
  if docker volume inspect "$v" > /dev/null 2>&1; then
    origem=$(docker volume inspect -f '{{ .Mountpoint }}' "$v")
    tar -czf "$TMP/$v.tar.gz" -C "$origem" .
    log "    $v ok ($(du -h "$TMP/$v.tar.gz" | cut -f1))"
  else
    log "    aviso: volume $v nao existe, pulando"
  fi
done

log "3/5 Configuracao do servidor"
LISTA=()
for f in opt/nfsesaas/NfseSaaS/docker-compose.prod.yml opt/nfsesaas/NfseSaaS/.env etc/nginx etc/letsencrypt; do
  [ -e "/$f" ] && LISTA+=("$f")
done
tar -czf "$TMP/config.tar.gz" -C / "${LISTA[@]}"
{
  echo "data_utc=$CARIMBO"
  echo "commit=$(git -C "$APP_DIR" rev-parse --short HEAD 2>/dev/null || echo desconhecido)"
} > "$TMP/versao.txt"
(cd "$TMP" && sha256sum ./* > SHA256SUMS)

log "4/5 Enviando para o OneDrive"
tar -cf "$LOCAL_DIR/$NOME" -C "$TMP" .
rm -rf "$TMP"; TMP=""
chmod 600 "$LOCAL_DIR/$NOME"
rclone copy "$LOCAL_DIR/$NOME" "${REMOTO}diario/" --retries 5 --low-level-retries 20
SEMANAIS=$(rclone lsf "${REMOTO}semanal/" --files-only 2>/dev/null || true)
if [ "$(date -u +%u)" = "7" ] || [ -z "$SEMANAIS" ]; then
  rclone copy "$LOCAL_DIR/$NOME" "${REMOTO}semanal/" --retries 5 --low-level-retries 20
  log "    copia semanal enviada"
fi
log "    enviado ok ($(du -h "$LOCAL_DIR/$NOME" | cut -f1))"

log "5/5 Limpando backups antigos"
limpar() {
  local pasta=$1 manter=$2 lista
  lista=$(rclone lsf "${REMOTO}${pasta}/" --files-only 2>/dev/null || true)
  printf '%s\n' "$lista" | grep -E '^nfsesaas-.*\.tar$' | sort | head -n -"$manter" | while read -r f; do
    rclone deletefile "${REMOTO}${pasta}/$f" && log "    removido ${pasta}/$f"
  done || true
}
limpar diario  "$MANTER_DIARIOS"
limpar semanal "$MANTER_SEMANAIS"
find "$LOCAL_DIR" -maxdepth 1 -name 'nfsesaas-*.tar' -printf '%f\n' | sort | head -n -"$MANTER_LOCAIS" | while read -r f; do
  rm -f "$LOCAL_DIR/$f"
done || true

log "===== SUCESSO: $NOME ====="
