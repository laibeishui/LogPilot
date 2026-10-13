#!/usr/bin/env bash
# 发布 Linux 运行目录（framework-dependent）
# 在仓库根目录执行： bash deploy/linux/publish.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${ROOT}/artifacts/linux-x64"
mkdir -p "${OUT}/data"

echo "Publishing to ${OUT} ..."
dotnet publish "${ROOT}/src/LogPilot.Web/LogPilot.Web.csproj" \
  -c Release \
  -o "${OUT}" \
  /p:UseAppHost=true

cat > "${OUT}/env.example" <<'EOF'
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
Storage__Sqlite__DataSource=./data/logpilot.db
Storage__VictoriaLogs__BaseUrl=http://127.0.0.1:9428
EOF

echo "Done."
echo "  1) Start VictoriaLogs: docker compose -f deploy/compose.vl-only.yml up -d"
echo "  2) Run:               bash deploy/linux/run.sh"
echo "  3) Or install service: see deploy/linux/logpilot.service"
