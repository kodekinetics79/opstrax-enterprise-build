#!/usr/bin/env bash
# Provision (or update) an OpsTrax telematics device edge on a Debian/Ubuntu VPS.
#
# Idempotent: safe to re-run to upgrade a build or re-apply configuration.
#
#   sudo OPSTRAX_GATEWAY_SECRET="$(cat /root/gateway.secret)" ./install.sh \
#        --gateway-id khalid-gw-1 \
#        --base-url  https://opstrax-enterprise-build.onrender.com \
#        --allowlist /root/imei-allowlist.txt
#
# THE SECRET IS NEVER ACCEPTED AS A COMMAND-LINE ARGUMENT. argv is world-readable through
# /proc/<pid>/cmdline for the whole life of the process and lands in shell history, so a
# secret passed that way is exposed to every local account. Supply it through one of:
#   - the OPSTRAX_GATEWAY_SECRET environment variable (as above), or
#   - --secret-file <path>: a root-readable file whose first line is the secret.
# On re-runs, omit both to keep the secret already installed in /etc/opstrax/gateway.env.
#
# THE ALLOWLIST IS NEVER SEEDED FROM THE EXAMPLE FILE. The example ships only syntactically
# inadmissible placeholders; the real list must name the IMEIs of the devices YOU provisioned
# (the values recorded at device commissioning in OpsTrax — Fleet -> Devices, i.e. the
# eld_devices rows created via POST /api/telemetry/devices — or your shipping manifest).
# Pass it with --allowlist <path>; an existing /etc/opstrax/imei-allowlist.txt is kept.
#
# Publish the gateway first, from the REPOSITORY ROOT:
#   dotnet publish telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj \
#     -c Release -o ./publish
#   rsync -a ./publish/ root@<edge-ip>:/opt/opstrax/gateway/
#   rsync -a telematics/deploy/ root@<edge-ip>:/opt/opstrax/deploy/

set -euo pipefail

GATEWAY_ID=""
BASE_URL=""
SECRET_FILE=""
ALLOWLIST_SRC=""
LISTEN_PORT="5023"
APP_DIR="/opt/opstrax/gateway"
CONF_DIR="/etc/opstrax"
STATE_DIR="/var/lib/opstrax-gateway"
SERVICE="opstrax-telematics-gateway"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --gateway-id)  GATEWAY_ID="$2";    shift 2 ;;
    --base-url)    BASE_URL="$2";      shift 2 ;;
    --secret-file) SECRET_FILE="$2";   shift 2 ;;
    --allowlist)   ALLOWLIST_SRC="$2"; shift 2 ;;
    --port)        LISTEN_PORT="$2";   shift 2 ;;
    *)
      echo "unknown argument: $1" >&2
      echo "The gateway secret is never accepted on the command line. Supply it via the" >&2
      echo "OPSTRAX_GATEWAY_SECRET environment variable or --secret-file <path>." >&2
      exit 2 ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "install.sh must run as root." >&2; exit 1; }
for required in GATEWAY_ID BASE_URL; do
  [[ -n "${!required}" ]] || { echo "--${required,,} is required (see the header of this script)." >&2; exit 2; }
done
[[ "$BASE_URL" == https://* ]] || { echo "--base-url must be https." >&2; exit 2; }

# ── Resolve the gateway secret (environment or file — NEVER argv) ─────────────
SECRET="${OPSTRAX_GATEWAY_SECRET:-}"
if [[ -z "$SECRET" && -n "$SECRET_FILE" ]]; then
  [[ -f "$SECRET_FILE" && -r "$SECRET_FILE" ]] \
    || { echo "--secret-file: '$SECRET_FILE' does not exist or is not readable." >&2; exit 2; }
  IFS= read -r SECRET < "$SECRET_FILE" || true
fi

if [[ -z "$SECRET" && ! -f "$CONF_DIR/gateway.env" ]]; then
  echo "No gateway secret. Provision one with POST /api/telemetry/gateways (shown exactly once)," >&2
  echo "then supply it via the OPSTRAX_GATEWAY_SECRET environment variable or --secret-file <path>." >&2
  echo "It is deliberately not accepted as a command-line argument: argv is world-readable via /proc." >&2
  exit 2
fi

# The server refuses a stored secret shorter than 32 characters, so a shorter one here can only
# ever produce 503s. Catch it now rather than in a log at 3am.
if [[ -n "$SECRET" ]]; then
  [[ ${#SECRET} -ge 32 ]] || { echo "The gateway secret must be at least 32 characters." >&2; exit 2; }
fi

# ── Resolve the IMEI allowlist (explicit file or an existing installed one) ───
# FAIL-CLOSED BY DESIGN: the gateway admits nothing without an allowlist, and this installer
# refuses to invent one. The old behaviour of seeding from imei-allowlist.example.txt is exactly
# how an unrelated (or attacker-known) IMEI ends up admitted on a public port.
if [[ -n "$ALLOWLIST_SRC" ]]; then
  [[ -f "$ALLOWLIST_SRC" && -r "$ALLOWLIST_SRC" ]] \
    || { echo "--allowlist: '$ALLOWLIST_SRC' does not exist or is not readable." >&2; exit 2; }
elif [[ ! -f "$CONF_DIR/imei-allowlist.txt" ]]; then
  echo "No IMEI allowlist. The edge fails closed: without one it admits NO devices." >&2
  echo "Pass --allowlist <path> pointing at a file listing the IMEIs of the devices you" >&2
  echo "provisioned in OpsTrax (Fleet -> Devices / POST /api/telemetry/devices — the device" >&2
  echo "commissioning records are the authoritative source; a shipping manifest also works)." >&2
  echo "Format reference: telematics/deploy/imei-allowlist.example.txt — a TEMPLATE whose" >&2
  echo "placeholders are deliberately inadmissible. It is never installed automatically." >&2
  exit 2
fi

echo "==> Installing the .NET 8 runtime"
if ! command -v dotnet >/dev/null 2>&1; then
  apt-get update -qq
  apt-get install -y -qq ca-certificates curl gnupg
  # Microsoft's package feed, keyed to this host's distro release.
  source /etc/os-release
  curl -fsSL "https://packages.microsoft.com/config/${ID}/${VERSION_ID}/packages-microsoft-prod.deb" \
    -o /tmp/packages-microsoft-prod.deb
  dpkg -i /tmp/packages-microsoft-prod.deb
  rm -f /tmp/packages-microsoft-prod.deb
  apt-get update -qq
  apt-get install -y -qq dotnet-runtime-8.0
fi

echo "==> Disciplining the system clock"
# The HMAC timestamp must land within +/-300s of OpsTrax. An undisciplined clock presents as a
# wave of 401s with a perfectly valid secret — one of the hardest failures to diagnose from logs.
apt-get install -y -qq systemd-timesyncd >/dev/null 2>&1 || true
timedatectl set-ntp true || true

echo "==> Creating the service account and directories"
id -u opstrax >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin opstrax
install -d -o root    -g opstrax -m 0750 "$CONF_DIR"
install -d -o opstrax -g opstrax -m 0750 "$STATE_DIR"
# The outbox holds encrypted vehicle fixes; owner-only, and the gateway re-asserts 0700 at boot.
install -d -o opstrax -g opstrax -m 0700 "$STATE_DIR/outbox"
install -d -o root    -g opstrax -m 0755 "$APP_DIR"

echo "==> Writing configuration"
install -o root -g opstrax -m 0640 "$HERE/appsettings.Production.json" "$APP_DIR/appsettings.Production.json"
sed -i \
  -e "s|\"GatewayId\": \"REPLACE-ME\"|\"GatewayId\": \"${GATEWAY_ID}\"|" \
  -e "s|\"BaseUrl\": \"[^\"]*\"|\"BaseUrl\": \"${BASE_URL}\"|" \
  -e "s|\"ListenPort\": 5023|\"ListenPort\": ${LISTEN_PORT}|" \
  "$APP_DIR/appsettings.Production.json"

# ── Secrets: /etc/opstrax/gateway.env, mode 0640 root:opstrax, umask 077 while writing ────────
# Consumed via systemd EnvironmentFile so nothing here ever appears in `systemctl cat`, argv,
# or a config repo. Two entries live in it:
#   Gateway__Edge__Forward__Secret       — the per-gateway HMAC secret (provisioned by OpsTrax)
#   Gateway__StoreForwardEncryptionKey   — AES-256 key for the on-disk outbox; generated here on
#                                          first install and PRESERVED on re-runs, because parked
#                                          fixes written under the old key would otherwise be
#                                          dropped as corrupt. See
#                                          docs/telematics/security/OUTBOX_KEY_MANAGEMENT.md.
OUTBOX_KEY=""
if [[ -f "$CONF_DIR/gateway.env" ]]; then
  OUTBOX_KEY="$(grep -E '^Gateway__StoreForwardEncryptionKey=' "$CONF_DIR/gateway.env" | head -n1 | cut -d= -f2- || true)"
fi
if [[ -z "$OUTBOX_KEY" ]]; then
  OUTBOX_KEY="$(head -c 32 /dev/urandom | base64 | tr -d '\n')"
fi

umask 077
if [[ -n "$SECRET" ]]; then
  cat > "$CONF_DIR/gateway.env" <<ENVEOF
Gateway__Edge__Forward__Secret=${SECRET}
Gateway__StoreForwardEncryptionKey=${OUTBOX_KEY}
ENVEOF
  chown root:opstrax "$CONF_DIR/gateway.env"
  chmod 0640 "$CONF_DIR/gateway.env"
else
  echo "    Keeping the existing $CONF_DIR/gateway.env (no new secret supplied)."
  if ! grep -q '^Gateway__StoreForwardEncryptionKey=' "$CONF_DIR/gateway.env"; then
    printf 'Gateway__StoreForwardEncryptionKey=%s\n' "$OUTBOX_KEY" >> "$CONF_DIR/gateway.env"
    echo "    Added the outbox encryption key to gateway.env (required since the encrypted-outbox change)."
  fi
fi

if [[ -n "$ALLOWLIST_SRC" ]]; then
  install -o root -g opstrax -m 0640 "$ALLOWLIST_SRC" "$CONF_DIR/imei-allowlist.txt"
  echo "    Installed $CONF_DIR/imei-allowlist.txt from ${ALLOWLIST_SRC}."
else
  echo "    Keeping the existing $CONF_DIR/imei-allowlist.txt."
fi

echo "==> Opening the device port"
if command -v ufw >/dev/null 2>&1; then
  # Trackers roam across carrier NAT, so the tracker port cannot be pinned to a source range in
  # general. The IMEI allowlist is what keeps the open port from being an open relay; if the
  # carrier publishes its egress ranges, narrow this rule to them as defence in depth.
  ufw allow "${LISTEN_PORT}/tcp" comment 'OpsTrax tracker edge' || true
fi

echo "==> Installing the service"
install -o root -g root -m 0644 "$HERE/${SERVICE}.service" "/etc/systemd/system/${SERVICE}.service"
systemctl daemon-reload
systemctl enable "$SERVICE"
systemctl restart "$SERVICE"

sleep 3
if systemctl is-active --quiet "$SERVICE"; then
  echo "==> Running. Listening on ${LISTEN_PORT}/tcp."
else
  echo "==> FAILED to start. The gateway refuses to boot on bad forwarding configuration:" >&2
  journalctl -u "$SERVICE" -n 30 --no-pager >&2
  exit 1
fi

PUBLIC_IP="$(curl -fsS --max-time 5 https://checkip.amazonaws.com 2>/dev/null || echo '<this host>')"
cat <<SUMMARY

Device edge ready.

  Point the tracker at:   ${PUBLIC_IP%$'\n'}:${LISTEN_PORT}
  Verify reachability:    nc -vz ${PUBLIC_IP%$'\n'} ${LISTEN_PORT}
  Allowlist a device:     \$EDITOR ${CONF_DIR}/imei-allowlist.txt      (no restart needed)
  Watch it work:          journalctl -u ${SERVICE} -f

Confirm the IP above is the STATIC address (Elastic IP / reserved IP), not an ephemeral one.
A tracker's SERVER command bakes it in, and re-provisioning a deployed unit means physical access.
SUMMARY
