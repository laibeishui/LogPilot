#!/usr/bin/env bash
# 安装 systemd 服务（需 root）
# 前置：已安装 aspnetcore-runtime-8.0；已执行 publish.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="${ROOT}/artifacts/linux-x64"
DEST="/opt/logpilot"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "请使用 root 运行: sudo bash deploy/linux/install-service.sh" >&2
  exit 1
fi

if [[ ! -f "${SRC}/LogPilot.Web.dll" ]]; then
  echo "请先执行: bash deploy/linux/publish.sh" >&2
  exit 1
fi

id -u logpilot >/dev/null 2>&1 || useradd --system --home "${DEST}" --shell /usr/sbin/nologin logpilot

mkdir -p "${DEST}/data"
rsync -a --delete --exclude 'data/' "${SRC}/" "${DEST}/"
mkdir -p "${DEST}/data"
chown -R logpilot:logpilot "${DEST}"

install -m 0644 "${ROOT}/deploy/linux/logpilot.service" /etc/systemd/system/logpilot.service
systemctl daemon-reload
systemctl enable --now logpilot.service
systemctl --no-pager --full status logpilot.service || true

echo "已安装并启动 logpilot.service"
echo "日志: journalctl -u logpilot -f"
echo "健康: curl -s http://127.0.0.1:8080/health/live"
