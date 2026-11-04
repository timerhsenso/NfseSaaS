#!/usr/bin/env bash
# Certificado que criptografa as chaves do Data Protection na VPS.
# Ver docs/operacao/protecao-chaves.md.
#
#   bash deploy/protecao-chaves/configurar.sh              -> cria (só uma vez)
#   bash deploy/protecao-chaves/configurar.sh --restaurar  -> recria a partir do
#                                                            gerenciador de senhas
#
# Gera (ou restaura):
#   /opt/nfsesaas/segredos/dp-protecao.pfx          certificado + chave privada (com senha)
#   /opt/nfsesaas/NfseSaaS/docker-compose.segredos.yml   monta o .pfx no app e passa a senha
#
# Os dois ficam FORA do git e FORA do backup diário: o backup leva as chaves
# do Data Protection, e quem tiver só o backup não pode ter também o que
# abre essas chaves. O .pfx e a senha vão para o gerenciador de senhas.
set -Eeuo pipefail

APP_DIR=/opt/nfsesaas/NfseSaaS
SEG_DIR=/opt/nfsesaas/segredos
PFX="$SEG_DIR/dp-protecao.pfx"
OVERRIDE="$APP_DIR/docker-compose.segredos.yml"
PFX_NO_CONTAINER=/run/segredos/dp-protecao.pfx

falhar() { echo; echo "ERRO: $*"; exit 1; }

[ "$(id -u)" -eq 0 ] || falhar "rode como root."
[ -d "$APP_DIR" ] || falhar "pasta $APP_DIR não encontrada."
command -v openssl >/dev/null || falhar "openssl não instalado."

# Nunca sobrescrever: trocar o .pfx depois que as chaves foram criptografadas
# com ele deixa TODOS os certificados dos clientes ilegíveis.
if [ -e "$PFX" ] || [ -e "$OVERRIDE" ]; then
  falhar "já existe $PFX ou $OVERRIDE. Nada foi alterado. (Para refazer do zero, fale com o suporte antes — apagar esses arquivos pode tornar os certificados ilegíveis.)"
fi

umask 077
mkdir -p "$SEG_DIR"
chmod 700 "$SEG_DIR"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

if [ "${1:-}" = "--restaurar" ]; then
  echo "Cole a SENHA do certificado de proteção (não aparece na tela) e tecle Enter:"
  read -rs SENHA; echo
  [ -n "$SENHA" ] || falhar "senha vazia."
  echo "Cole o texto do arquivo (as linhas entre INICIO e FIM) e tecle Enter numa linha vazia:"
  : > "$TMP/pfx.b64"
  while IFS= read -r linha; do
    [ -z "$linha" ] && break
    printf '%s\n' "$linha" >> "$TMP/pfx.b64"
  done
  base64 -d "$TMP/pfx.b64" > "$TMP/dp-protecao.pfx" 2>/dev/null || falhar "o texto colado não é um base64 válido."
else
  SENHA=$(openssl rand -hex 24)
  openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
    -subj "/CN=NfseSaaS DataProtection" \
    -keyout "$TMP/chave.pem" -out "$TMP/cert.pem" 2>/dev/null
  SENHA="$SENHA" openssl pkcs12 -export -inkey "$TMP/chave.pem" -in "$TMP/cert.pem" \
    -out "$TMP/dp-protecao.pfx" -passout env:SENHA
fi

# Confere senha + conteúdo antes de instalar qualquer coisa.
SENHA="$SENHA" openssl pkcs12 -in "$TMP/dp-protecao.pfx" -passin env:SENHA -noout 2>/dev/null \
  || falhar "o .pfx não abriu com essa senha. Nada foi instalado."
[[ "$SENHA" =~ ^[A-Za-z0-9]+$ ]] || falhar "a senha só pode ter letras e números (é a gerada por este script)."

install -m 444 "$TMP/dp-protecao.pfx" "$PFX"

cat > "$OVERRIDE" <<EOF
# Gerado por deploy/protecao-chaves/configurar.sh em $(date -u +%FT%TZ).
# NÃO versionar e NÃO apagar. Ver docs/operacao/protecao-chaves.md.
services:
  app:
    volumes:
      - $PFX:$PFX_NO_CONTAINER:ro
    environment:
      DataProtection__CertificadoPfxPath: "$PFX_NO_CONTAINER"
      DataProtection__CertificadoSenha: "$SENHA"
EOF
chmod 600 "$OVERRIDE"

cd "$APP_DIR"
docker compose -f docker-compose.prod.yml -f docker-compose.segredos.yml config --quiet \
  || { rm -f "$PFX" "$OVERRIDE"; falhar "o docker compose recusou a configuração. Nada ficou instalado."; }

IMPRESSAO=$(openssl x509 -in <(SENHA="$SENHA" openssl pkcs12 -in "$PFX" -passin env:SENHA -nokeys 2>/dev/null) -noout -fingerprint -sha1 | cut -d= -f2 | tr -d ':')

echo
echo "=============================================================="
echo " PRONTO. Certificado de proteção instalado."
echo " Impressão digital (thumbprint): $IMPRESSAO"
echo "=============================================================="
if [ "${1:-}" != "--restaurar" ]; then
  echo
  echo " Guarde AGORA no gerenciador de senhas, num item novo"
  echo " \"micoempresa - certificado de proteção das chaves\":"
  echo
  echo " 1) SENHA:"
  echo "    $SENHA"
  echo
  echo " 2) ARQUIVO (copie tudo entre INICIO e FIM, sem essas duas palavras):"
  echo "----- INICIO -----"
  base64 -w 76 "$PFX"
  echo "----- FIM -----"
  echo
  echo " Sem esses dois, um backup restaurado NÃO abre os certificados dos clientes."
  echo " Não cole isso em chat, e-mail ou no Git."
fi
echo
echo " Próximo passo: rodar o Deploy Production no GitHub."
