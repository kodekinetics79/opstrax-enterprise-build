#!/usr/bin/env bash
# Provision (or update) an OpsTrax telematics device edge on a Debian/Ubuntu VPS.
#
# Idempotent: safe to re-run to upgrade a build or re-apply configuration.
#
#   sudo ./install.sh --gateway-id khalid-gw-1 \
#                     --base-url https://opstrax-enterprise-build.onrender.com \
#                     --secret "$GATEWAY_SECRET"
#
# Publish the gateway first, from the REPOSITORY ROOT:
#   dotnet publish telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj \
#     -c Release -o ./publish
#   rsync -a ./publish/ root@<edge-ip>:/opt/opstrax/gateway/
#   rsync -a telematics/deploy/ root@<edge-ip>:/opt/opstrax/deploy/

set -euo pipefail

GATEWAY_ID=""
BASE_URL=""
SECRET=""
LISTEN_PORT="5023"
APP_DIR="/opt/opstrax/gateway"
CONF_DIR="/etc/opstrax"
STATE_DIR="/var/lib/opstrax-gateway"
SERVICE="opstrax-telematics-gateway"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --gateway-id) GATEWAY_ID="$2"; shift 2 ;;
    --base-url)   BASE_URL="$2";   shift 2 ;;
    --secret)     SECRET="$2";     shift 2 ;;
    --port)       LISTEN_PORT="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "install.sh must run as root." >&2; exit 1; }
for required in GATEWAY_ID BASE_URL SECRET; do
  [[ -n "${!required}" ]] || { echo "--${required,,} is required (see the header of this script)." >&2; exit 2; }
done

# The server refuses a stored secret shorter than 32 characters, so a shorter one here can only
# ever produce 503s. Catch it now rather than in a log at 3am.
[[ ${#SECRET} -ge 32 ]] || { echo "The gateway secret must be at least 32 characters." >&2; exit 2; }
[[ "$BASE_URL" == https://* ]] || { echo "--base-url must be https." >&2; exit 2; }

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
install -d -o opstrax -g opstrax -m 0750 "$STATE_DIR" "$STATE_DIR/outbox"
install -d -o root    -g opstrax -m 0755 "$APP_DIR"

echo "==> Writing configuration"
install -o root -g opstrax -m 0640 "$HERE/appsettings.Production.json" "$APP_DIR/appsettings.Production.json"
sed -i \
  -e "s|\"GatewayId\": \"REPLACE-ME\"|\"GatewayId\": \"${GATEWAY_ID}\"|" \
  -e "s|\"BaseUrl\": \"[^\"]*\"|\"BaseUrl\": \"${BASE_URL}\"|" \
  -e "s|\"ListenPort\": 5023|\"ListenPort\": ${LISTEN_PORT}|" \
  "$APP_DIR/appsettings.Production.json"

# The secret lives only here: mode 0600 root:opstrax, referenced by EnvironmentFile so it never
# appears in `systemctl cat` or a config repo.
umask 077
cat > "$CONF_DIR/gateway.env" <<ENVEOF
Gateway__Edge__Forward__Secret=${SECRET}
ENVEOF
chown root:opstrax "$CONF_DIR/gateway.env"
chmod 0640 "$CONF_DIR/gateway.env"

if [[ ! -f "$CONF_DIR/imei-allowlist.txt" ]]; then
  install -o root -g opstrax -m 0640 "$HERE/imei-allowlist.example.txt" "$CONF_DIR/imei-allowlist.txt"
  echo "    NOTE: $CONF_DIR/imei-allowlist.txt seeded from the example. Edit it before connecting devices."
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
