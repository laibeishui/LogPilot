#!/usr/bin/env bash
# 前台运行已发布的 LogPilot（Linux）
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_DIR="${ROOT}/artifacts/linux-x64"

if [[ ! -f "${APP_DIR}/LogPilot.Web.dll" ]]; then
  echo "未找到发布产物，请先执行: bash deploy/linux/publish.sh" >&2
  exit 1
fi

mkdir -p "${APP_DIR}/data"
cd "${APP_DIR}"

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:8080}"
export Storage__Sqlite__DataSource="${Storage__Sqlite__DataSource:-./data/logpilot.db}"
export Storage__VictoriaLogs__BaseUrl="${Storage__VictoriaLogs__BaseUrl:-http://127.0.0.1:9428}"

echo "LogPilot listening on ${ASPNETCORE_URLS}"
echo "VictoriaLogs -> ${Storage__VictoriaLogs__BaseUrl}"
exec dotnet ./LogPilot.Web.dll
